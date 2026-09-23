# Phase 1 Tables - Full Definitions (Consolidated)

Every **Phase 1** table documented in this vault, with its full column list. Compiled from the canonical files in `database/schemas/` and `developer-platform/database/schema.md` (Phase 2 tables excluded per each file's `**Phase:**` markers; retained Phase 2 catalog references are not table-creation permission).

**Phase 1 total: 250 tables** (243 core + 7 Developer Platform Extensions - see below)

> **2026-08-03 revision:** Foundation + Projects + Objectives dropped from 17 to 14 tables — `workspaces`/`workspace_roles`/`workspace_members` removed (Workspace functionality is out of scope; see the Work Management implementation decisions) and `objective_participants` removed (Objective participation is represented only through `project_members`); `version_statuses` added (new global lookup, seeded `planned`/`released`/`archived`). Net -3 vs. the prior count. See the Foundation + Projects + Objectives section for the full column-level rework of `projects`, `project_members`, `project_member_invitations`, `versions`, `release_calendar`, `labels`, and `project_categories`.
>
> **2026-08-08 revision:** Foundation + Projects + Objectives moves from 14 to 15 tables — `objective_change_requests` (shipped 2026-08-04/05 with the milestone-hierarchy plan, extended 2026-08-08 with `achieve`/`unachieve` request types) had never been added to this inventory; documented now alongside `projects.is_achieved`/`achieved_at`, `objectives.is_achieved`/`achieved_at`, and `objectives.reporting_manager_id` (also undocumented since 2026-08-04/05 — see the Achieve workflow design, `specs/finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve-design.md`).

| Group | Modules (tables) | Total |
|:---|:---|---:|
| Pillar 1 - HR Management | Infrastructure (13), Auth & Security (20), Org Structure (8), Core HR (14), Time Off (7), Calendar (5), Configuration (11) | 78 |
| Pillar 2 - Monitoring | Activity Monitoring (8), Discrepancy Engine (3), Time & Attendance (18), Identity Verification (8), Productivity Analytics (5) | 42 |
| Pillar 3 - Work Management | Foundation + Projects + Objectives (15), Task Management + Worklogs (15), Sprint Planning (5), Collaboration (5), GitHub Repository Integration (6) | 46 |
| Shared Foundation | Shared Platform (54), Agent Gateway (6), Reporting Engine (3) | 63 |
| Developer Platform | Platform users/credentials/sessions/RBAC/auth events (9), System Config provider catalog/service keys (2), Platform OAuth app registration and secret rotation (2), Platform alerts (1) | 14 |
| Developer Platform Extensions | Demo Profile/Request approval flow (4), Subscription plan modules/add-ons/pricing (3) | 7 |

**Excluded as Phase 2:** Exception Engine (5), Workflow/Automation Engine (12, including Work repository automation through `task_automation_rules`), Microsoft Teams integration (8, as a Work Chat capability), `integration_connections`, `project_workspaces`, Chat + Chat AI (9), Payroll (12, including Compensation Setup), Skills & Learning (20, including Qualification Tracking), Grievance (2), Expense (3), IDE Extension (5), Customize Dashboard (design pending; no committed tables), agent release/ring tables (3), `platform_api_keys`, `overtime_records` (Overtime feature deferred to Phase 2), `ai_provider_configs` + `tenant_ai_provider_overrides` (AI is used only for Agentic Chat, and Agentic Chat is Phase 2).

---

# Pillar 1 - HR Management

## Infrastructure (13 tables)

### `countries`

Global reference list of countries (ISO codes, phone codes, currencies) used for nationality, statutory context, and holiday lookups.

| Column          | Type           | Notes              |
| :-------------- | :------------- | :----------------- |
| `id`            | `uuid`         | PK                 |
| `name`          | `varchar(100)` |                    |
| `code`          | `varchar(3)`   | ISO 3166-1 alpha-3 |
| `phone_code`    | `varchar(10)`  |                    |
| `currency_code` | `varchar(3)`   |                    |
|                 |                |                    |

### `approval_statuses`

Global seeded reference values for approval state across tenant/admin approval-like records.

Columns:
- `id` uuid PK
- `code` varchar(50) unique, e.g. `pending`, `approved`, `rejected`, `cancelled`
- `name` varchar(100)
- `description` text nullable
- `sort_order` integer
- `is_active` boolean
- `created_at` timestamptz
- `updated_at` timestamptz nullable

### `employment_statuses`

Global seeded reference values for employee employment status.

Columns:
- `id` uuid PK
- `code` varchar(50) unique, e.g. `onboarding`, `active`, `on_leave`, `offboarding`, `suspended`, `terminated`, `resigned`
- `name` varchar(100)
- `description` text nullable
- `sort_order` integer
- `is_active` boolean
- `created_at` timestamptz
- `updated_at` timestamptz nullable

### `employment_types`

Global seeded reference values for employment type.

Columns:
- `id` uuid PK
- `code` varchar(50) unique, e.g. `full_time`, `part_time`, `contract`, `intern`
- `name` varchar(100)
- `description` text nullable
- `sort_order` integer
- `is_active` boolean
- `created_at` timestamptz
- `updated_at` timestamptz nullable

### `severities`

Global seeded reference values for severity labels used by notifications, monitoring, discrepancy, alerts, validation, and platform health.

Columns:
- `id` uuid PK
- `code` varchar(50) unique, e.g. `info`, `warning`, `low`, `high`, `critical`, `blocker`
- `name` varchar(100)
- `description` text nullable
- `sort_order` integer
- `is_active` boolean
- `created_at` timestamptz
- `updated_at` timestamptz nullable

### `work_modes`

Global seeded reference values for employee work mode.

Columns:
- `id` uuid PK
- `code` varchar(50) unique, e.g. `onsite`, `remote`, `hybrid`
- `name` varchar(100)
- `description` text nullable
- `sort_order` integer
- `is_active` boolean
- `created_at` timestamptz
- `updated_at` timestamptz nullable

Rules:
- These tables are global seeded reference data.
- They do not have tenant_id.
- Tenant-specific customization is not part of this decision.
- Business tables may store the stable code value as varchar and validate against these seeded references at application/service boundary.
- Do not add job_levels. Position is the canonical seat/job model.

### `file_records`

Central registry of every uploaded file's Cloudflare R2 object key, size, MIME type, and lifecycle status; all other tables reference files through this table instead of storing object keys themselves.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `storage_key` | `varchar(700)` | Cloudflare R2 object key |
| `original_file_name` | `varchar(255)` | User-provided filename |
| `safe_file_name` | `varchar(255)` | Sanitized filename used in the R2 key |
| `content_type` | `varchar(150)` | MIME type |
| `detected_content_type` | `varchar(150)` | Nullable server/scan-detected MIME type |
| `file_size_bytes` | `bigint` | Actual stored file size |
| `checksum_sha256` | `varchar(64)` | Server-verified SHA-256 digest |
| `uploaded_by_user_id` | `uuid` | FK -> users |
| `status` | `varchar(30)` | `pending_scan`, `available`, `quarantined`, `deleted` |
| `scan_provider` | `varchar(50)` | Nullable scanner identifier |
| `scan_result_code` | `varchar(100)` | Nullable safe scan result code |
| `scan_completed_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |
| `deleted_at` | `timestamptz` | Nullable |
| `storage_deleted_at` | `timestamptz` | Nullable R2 deletion confirmation |

### `file_upload_reservations`

Quota reservation rows created before direct or server-mediated upload so concurrent uploads cannot exceed purchased tenant storage.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `reserved_bytes` | `bigint` | Expected upload size |
| `status` | `varchar(30)` | `active`, `completed`, `expired`, `cancelled` |
| `reserved_by_user_id` | `uuid` | FK -> users |
| `expires_at` | `timestamptz` | Required cleanup deadline |
| `completed_file_record_id` | `uuid` | Nullable FK -> file_records |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `tenant_storage_stats`

Cached tenant storage usage used for fast quota checks. Purchased limits are resolved in `tenant_resource_limits`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `tenant_id` | `uuid` | PK, FK -> tenants |
| `used_r2_bytes` | `bigint` | Bytes used by active R2-backed files |
| `used_db_bytes` | `bigint` | Bytes used by DB-backed file/content storage, if any |
| `reserved_r2_bytes` | `bigint` | Sum of active upload reservations |
| `last_calculated_at` | `timestamptz` | Last full recalculation |
| `updated_at` | `timestamptz` | |

### `outbox_messages`

Transactional delivery record written atomically with business mutations that require durable post-commit side effects.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK and consumer deduplication identifier |
| `tenant_id` | `uuid` | Nullable FK -> tenants; null only for platform-global operations |
| `event_type` | `varchar(200)` | Stable backend-owned event/operation code |
| `schema_version` | `integer` | Payload contract version |
| `payload` | `jsonb` | Minimum required safe payload; no secrets/raw tokens/unnecessary PII |
| `status` | `varchar(20)` | `pending`, `processing`, `published`, `failed` |
| `attempt_count` | `integer` | Starts at 0 |
| `next_attempt_at` | `timestamptz` | Retry eligibility timestamp |
| `locked_at` | `timestamptz` | Nullable worker lease timestamp |
| `locked_by` | `varchar(100)` | Nullable worker identifier |
| `published_at` | `timestamptz` | Nullable completion timestamp |
| `last_error_code` | `varchar(100)` | Nullable safe classification |
| `correlation_id` | `varchar(100)` | Request/job correlation identifier |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `entity_assets`

Generic links from normal product entities to files. Only for owners with no dedicated `*_file_id` column (reusable display assets and ordinary attachments only - not for monitoring/verification evidence files, and never for tenant/company logos, which have their own direct FK columns).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants; **required (not nullable) as first implemented** — the shared tenant query-filter builder in `ApplicationDbContext` reflects on a plain non-nullable `Guid TenantId` for every `ITenantOwnedEntity`, and this task never creates a platform-level row. Revisit as nullable only if/when a genuine platform-level (null-tenant) owner type is added. |
| `owner_type` | `varchar(50)` | Backend-owned discriminator, e.g. `tenant_user`, `platform_user`, `project`, `support_ticket`, `support_ticket_message`, `document` |
| `owner_id` | `uuid` | ID of the owning entity |
| `asset_purpose` | `varchar(50)` | Backend-owned purpose, e.g. `profile_photo`, `avatar`, `project_cover`, `attachment` |
| `file_record_id` | `uuid` | FK -> file_records |
| `is_primary` | `boolean` | Primary asset for the owner + purpose |
| `sort_order` | `integer` | Optional display ordering for attachments |
| `metadata` | `jsonb` | Safe non-secret metadata |
| `created_by_type` | `varchar(30)` | `user` or `platform_user` |
| `created_by_id` | `uuid` | Actor ID matching `created_by_type` |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Implementation status (2026-08-03):** documented since Phase 1 planning but not yet built in code (no table, no `owner_type`/`asset_purpose` constants existed in `src/` as of the Work Management audit). First implemented as part of the Work Management feature, scoped initially to `owner_type = 'project'` / `asset_purpose = 'project_cover'` (Project logo/cover) via centralized constants — see `docs/superpowers/plans/` for the Work Management foundation plan. Other owner types remain documented here as future scope, not yet wired to any handler.

### `tenants`

Root table of the multi-tenant platform - one row per customer company, holding profile, status, and subscription linkage. Nearly every tenant-scoped table (120+) references it.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `name` | `varchar(200)` | Company name |
| `slug` | `varchar(100)` | URL-safe identifier, UNIQUE |
| `primary_contact_email` | `varchar(255)` | Primary customer contact captured during Developer Platform tenant profile creation |
| `country_code` | `varchar(3)` | Tenant profile country code selected during provisioning |
| `industry_profile` | `varchar(30)` | `office_it`, `manufacturing`, `retail`, `healthcare`, `custom` - sets monitoring defaults during provisioning/demo approval |
| `registration_profile_name` | `varchar(200)` | Registration/profile display name captured on the tenant profile, not a legal entity name |
| `registration_number` | `varchar(50)` | Nullable registration/profile number captured on the tenant profile |
| `company_size_range` | `varchar(30)` | Employee-count range, e.g. `1-50`, `51-200`, `201-500` |
| `timezone` | `varchar(50)` | Tenant default IANA timezone selected during profile setup |
| `currency_code` | `varchar(3)` | Tenant default ISO 4217 currency selected during profile setup |
| `status` | `varchar(20)` | `provisioning`, `trial`, `trial_expired`, `pending_payment`, `active`, `suspended`, `cancelled` |
| `subscription_plan_id` | `uuid` | FK -> subscription_plans |
| `settings_json` | `jsonb` | Tenant-level settings |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `users`

Login accounts for tenant users - credentials, email verification, password setup/reset state. 1:1 with `employees` for staff members.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `email` | `varchar(255)` | Original validated Phase 1 ASCII address; trimmed before storage |
| `normalized_email` | `varchar(255)` | STORED generated `lower(trim(email))`; canonical login key |
| `password_hash` | `varchar(255)` | BCrypt hash, work factor 12 |
| `first_name` | `varchar(100)` |  |
| `last_name` | `varchar(100)` |  |
| `is_active` | `boolean` |  |
| `email_verified` | `boolean` |  |
| `last_login_at` | `timestamptz` | Nullable |
| `must_change_password` | `boolean` | Security reset flag; not used as an invite method |
| `password_setup_required` | `boolean` | True until invited user completes password setup; SSO can still be used when enabled |
| `password_setup_expires_at` | `timestamptz` | Nullable - expiry for account setup link; admin can resend invite |
| `password_reset_token_hash` | `varchar(128)` | Nullable - SHA-256 hex hash of the forgot-password reset token |
| `password_reset_token_expires_at` | `timestamptz` | Nullable - 1-hour expiry for password reset tokens |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` | Nullable |
| `is_deleted` | `boolean` | Soft delete |
| `created_by_id` | `uuid` | FK -> users (who created this record) |

**Email/index rules:** Internationalized domains are converted to lower-case IDNA/Punycode before storage. UNIQUE `(tenant_id, normalized_email)` is the canonical case/whitespace-insensitive tenant email guarantee. Existing raw-email uniqueness may remain as compatibility protection, but login correctness is based on `normalized_email`. Partial base-login lookup index: `btree(normalized_email, tenant_id, id) WHERE is_active = true AND is_deleted = false`.

**Base-login overflow rule:** Phase 1 does not enforce a database capacity cap for shared normalized emails. The locked `auth_internal.auth_lookup_base_login_candidates(normalized_email)` function returns up to nine deterministic internal candidates. The ninth row is an overflow probe: when nine or more eligible candidates exist, the application returns generic `401 invalid_credentials` and creates no workspace-selection challenge. No `tenant_auth_policies` login-method flag participates in base-login eligibility.

---

## Auth & Security (20 tables)

### `audit_logs`

Immutable who-did-what audit trail for every significant action in a tenant, with old/new value snapshots for compliance and investigations.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users (nullable for system actions) |
| `action` | `varchar(100)` | e.g., `employee.created`, `time_off.approved` |
| `resource_type` | `varchar(50)` | e.g., `Employee`, `TimeOffRequest` |
| `resource_id` | `uuid` |  |
| `old_values_json` | `jsonb` | Previous state |
| `new_values_json` | `jsonb` | New state |
| `ip_address` | `varchar(45)` |  |
| `correlation_id` | `uuid` | Request correlation |
| `created_at` | `timestamptz` |  |

### `feature_access_grants`

Narrows or exposes module/feature visibility per role or employee inside what the tenant is already commercially entitled to. Not a billing table.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `grantee_type` | `varchar(10)` | `role` or `employee` |
| `grantee_id` | `uuid` | FK -> roles.id OR users.id (polymorphic, depends on grantee_type; app-layer enforced) |
| `module` | `varchar(80)` | Module key, e.g. `core_hr`, `time_off`, `work_management` |
| `feature_key` | `varchar(120)` | Optional feature key inside the module, e.g. `time_off.requests` |
| `is_enabled` | `boolean` |  |
| `granted_by` | `uuid` | FK -> users |
| `valid_from` | `timestamptz` | Nullable; defaults to active immediately |
| `expires_at` | `timestamptz` | Nullable; use for temporary role/employee module or feature visibility |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

**Unique:** `(tenant_id, grantee_type, grantee_id, module, feature_key)`

### `legal_acceptance_records`

Records each user's acceptance, acknowledgement, or decline of legal documents (terms, privacy, monitoring/biometric notices) as compliance evidence.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users |
| `document_type` | `varchar(80)` | `terms`, `privacy_notice`, `activity_monitoring_notice`, `screenshot_notice`, `biometric_photo_consent`, `marketing` |
| `document_version` | `varchar(50)` | Version accepted/acknowledged; references published version string from Developer Platform `legal_document_versions` |
| `decision` | `varchar(20)` | `accepted`, `acknowledged`, `declined` |
| `required` | `boolean` | Evidence snapshot copied from `legal_document_versions.is_required`; never client-authoritative and not the current-gate source |
| `decided_at` | `timestamptz` |  |
| `ip_address` | `varchar(45)` |  |
| `user_agent` | `varchar(500)` |  |
| `source` | `varchar(30)` | `invite`, `web`, `desktop-agent` |
| `supersedes_record_id` | `uuid` | Nullable self-FK to the immediately preceding decision for the same tenant/user/document type/version |

**Relationship:** `(document_type, document_version)` identifies the published `(document_type, version)` in Phase 1 `legal_document_versions`. `legal_acceptance_records` is canonical product/documentation naming; `gdpr_consent_records` is legacy naming only.

**Append-only evidence and decision rules:** Every decision inserts an immutable row. A later decision for the same tenant/user/document type/version links to the preceding row through `supersedes_record_id`; it never overwrites prior evidence. The current decision is the latest by `(decided_at DESC, id DESC)`. Retry deduplication uses `Idempotency-Key`. `terms` requires `accepted`; `privacy_notice` requires `acknowledged`. The backend resolves the published document and copies `is_required`; it does not trust a client-supplied `required` value.

**Index and foreign keys:** Index `(tenant_id, user_id, document_type, document_version, decided_at DESC, id DESC)`. `(document_type, document_version)` -> `legal_document_versions(document_type, version)`; `supersedes_record_id` -> `legal_acceptance_records(id)`.

### `legal_login_challenges`

Durable server-side authority for the pre-session Legal & Privacy completion flow. The browser stores only the raw opaque handle in the HttpOnly `onevo_legal_pending` cookie.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users |
| `challenge_hash` | `varchar(128)` | UNIQUE; SHA-256 hash of opaque cookie handle |
| `csrf_token_hash` | `varchar(128)` | Hash of context-bound readable CSRF value |
| `origin` | `varchar(30)` | `password`, `mfa`, `google_sso`, `stale_session` |
| `expires_at` | `timestamptz` | Ten minutes after creation |
| `superseded_at` | `timestamptz` | Nullable |
| `superseded_by_id` | `uuid` | Nullable self-FK |
| `consumed_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` | NOT NULL |

**Indexes:** UNIQUE `challenge_hash`; `(tenant_id, user_id, expires_at)`; `expires_at`.

**Rules:** Only unexpired, unsuperseded, unconsumed matching rows authorize pending legal completion. Partial completion rotates rows transactionally. Final completion records acceptance, consumes the row, and creates the normal session transactionally. PostgreSQL is required in production/staging; raw handles/CSRF values are never stored, and `sessions`, `mfa_challenges`, and `outbox_messages` are not reused.

### `login_workspace_selection_challenges`

Durable server-side authority for the base-domain credential-first login flow when one normalized email/password pair matches active users in multiple login-eligible tenants.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `challenge_hash` | `varchar(128)` | UNIQUE; SHA-256 hash of the opaque response handle; raw value never stored |
| `normalized_email` | `varchar(255)` | Exact generated user normalized-email value bound to the verified candidate set |
| `candidate_workspaces_json` | `jsonb` | Server-only exact `{ tenant_id, user_id, slug, display_name }` candidate snapshot; never returned as-is |
| `purpose` | `varchar(40)` | NOT NULL; fixed to `workspace_selection` |
| `expires_at` | `timestamptz` | NOT NULL; five minutes after creation |
| `consumed_at` | `timestamptz` | Nullable; set atomically before selected-user login continues |
| `failed_attempt_count` | `integer` | NOT NULL, default 0; atomically incremented and consumed on the fifth invalid choice |
| `created_at` | `timestamptz` | NOT NULL |
| `ip_address` | `varchar(45)` | Nullable |
| `user_agent` | `varchar(500)` | Nullable |

**Indexes/constraints:** UNIQUE `challenge_hash`; `expires_at`; `(normalized_email, created_at)`; `purpose = 'workspace_selection'`; `failed_attempt_count BETWEEN 0 AND 5`.

**Rules:** Created only after credential verification produces two through eight matches among active/unlocked users in `active` or `trial` tenants. Password and Google base-login flows store the authentication origin in server-side challenge state so MFA, legal acceptance, and audit continuation preserve the correct origin. The response projects only slug/display name. The raw handle is opaque, five-minute, single-use, and valid only for `/api/v1/auth/login/select-workspace`; it is not a session/JWT/MFA/legal authority. Invalid-choice increments and fifth-failure consumption are one conditional atomic update; valid selection conditionally consumes the row before the existing per-user MFA and legal gates run. An Auth cleanup job runs at least hourly and hard-deletes expired/consumed rows after 24 hours. Tenant `audit_logs` starts only after tenant resolution; pre-tenant failures use structured platform security telemetry and threshold-driven global alerts. Do not reuse `sessions`, `mfa_challenges`, `legal_login_challenges`, or `outbox_messages` as workspace-challenge state; the separate platform-global abuse-alert intent may use the normal outbox boundary.

### `permissions`

Global catalog of permission codes (`resource:action`) that all RBAC checks (`[RequirePermission]`, `hasPermission`) resolve against.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `code` | `varchar(120)` | e.g., `employees:read`, `monitoring:read`, `monitoring:alerts:read` |
| `description` | `varchar(255)` |  |
| `module` | `varchar(80)` | Which module this permission belongs to |
| `feature_key` | `varchar(120)` | Nullable; set only when the permission is tied to a commercial feature key |

### `invitation_tokens`

Secure one-time invitation records. Raw invite tokens are never stored; only a SHA-256 hash is persisted.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users; pending invited account |
| `role_id` | `uuid` | Nullable FK -> roles; role to assign on acceptance; required for provisioning owner invites |
| `invited_email` | `varchar(255)` | Original email address the invite was sent to |
| `invited_full_name` | `varchar(255)` | Display name of invitee; used in invite email and accept flow |
| `token_hash` | `varchar(128)` | SHA-256 hash of invite token; never store raw token |
| `status` | `varchar(20)` | `pending`, `accepted`, `expired`, `revoked` |
| `completion_methods_json` | `jsonb` | Allowed methods: `password`, `google` |
| `completed_with` | `varchar(20)` | Nullable; `password` or `google` |
| `allow_google_email_mismatch` | `boolean` | Whether Google email may differ from invited email |
| `allowed_email_domains_json` | `jsonb` | Allowed domains for Google email mismatch |
| `expires_at` | `timestamptz` | Usually 72 hours after creation |
| `used_at` | `timestamptz` | Nullable; set when invite is completed |
| `revoked_at` | `timestamptz` | Nullable |
| `revoked_by_user_id` | `uuid` | Nullable FK -> users |
| `revoked_by_platform_user_id` | `uuid` | Nullable FK -> platform_users |
| `created_by_user_id` | `uuid` | Nullable FK -> users |
| `created_by_platform_user_id` | `uuid` | Nullable FK -> platform_users |
| `created_at` | `timestamptz` | |

### `tenant_auth_policies`

Tenant-level defaults for invitation Google mismatch rules. Password login and configured Google login are not gated by tenant-auth-policy availability flags in Phase 1.

| Column | Type | Notes |
|:-------|:-----|:------|
| `tenant_id` | `uuid` | PK, FK -> tenants |
| `invite_google_email_mismatch_allowed` | `boolean` | Default mismatch rule for invitations |
| `allowed_login_domains_json` | `jsonb` | Allowed email domains for SSO/Google mismatch |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | Nullable |

**MFA boundary:** Verified MFA enablement is per user in `user_mfa`; no tenant-wide MFA policy column is defined.

### `user_external_identities`

Links tenant users to external identity providers (Google in Phase 1) so provider identity details don't overload `users.email`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users |
| `provider` | `varchar(30)` | `google`; future providers may include SAML/OIDC |
| `provider_subject` | `varchar(255)` | Stable provider subject, e.g., Google `sub` |
| `provider_email` | `varchar(255)` | Verified email returned by provider |
| `email_verified` | `boolean` | Provider email verification state |
| `linked_at` | `timestamptz` | |
| `last_used_at` | `timestamptz` | Nullable |

**Unique:** `(tenant_id, provider, provider_subject)`, `(tenant_id, provider, user_id)`

### `role_permissions`

Join table granting permissions to roles - the core RBAC mapping.

| Column | Type | Notes |
|:-------|:-----|:------|
| `role_id` | `uuid` | FK -> roles |
| `permission_id` | `uuid` | FK -> permissions |

**PK:** `(role_id, permission_id)`

### `roles`

Tenant-scoped RBAC roles that bundle permissions for assignment to users (e.g., "HR Manager", "Employee").

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(100)` | e.g., "HR Manager", "CEO", "Employee" |
| `description` | `varchar(255)` |  |
| `is_system` | `boolean` | System roles can't be deleted |
| `source_template_id` | `uuid` | Nullable FK -> role_templates when materialized from a reusable template |
| `created_at` | `timestamptz` |  |

### `role_templates`

Developer Platform starter role definitions, materialized into tenant-scoped `roles` after validation against the tenant's enabled modules.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `template_key` | `varchar(100)` | Globally unique stable key, e.g. `tenant-owner`, `engineering-manager` |
| `name` | `varchar(100)` | e.g., `Tenant Owner`, `HR Admin`, `Time Off Manager` |
| `description` | `varchar(255)` | Nullable |
| `module_keys_json` | `jsonb` | Modules this template is intended for |
| `permission_codes_json` | `jsonb` | Permission codes included in the template |
| `is_system` | `boolean` | ONEVO default template |
| `version` | `integer` | Template version |
| `is_active` | `boolean` | |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | Nullable |

**Unique:** `template_key`

### `sessions`

Active login sessions per user, tracked for activity monitoring, expiry, and admin revocation.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `user_id` | `uuid` | FK -> users |
| `tenant_id` | `uuid` | FK -> tenants |
| `ip_address` | `varchar(45)` |  |
| `user_agent` | `varchar(500)` |  |
| `started_at` | `timestamptz` |  |
| `last_activity_at` | `timestamptz` |  |
| `expires_at` | `timestamptz` |  |
| `is_revoked` | `boolean` |  |

### `user_permission_overrides`

Per-user grant/revoke exceptions layered on top of role permissions, with reason and expiry for audit.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users |
| `permission_id` | `uuid` | FK -> permissions |
| `grant_type` | `varchar(10)` | `grant` or `revoke` |
| `reason` | `varchar(255)` | Why this override exists |
| `valid_from` | `timestamptz` | Nullable |
| `expires_at` | `timestamptz` | Nullable |
| `granted_by` | `uuid` | FK -> users (Super Admin who set this) |
| `created_at` | `timestamptz` |  |

**Unique:** `(tenant_id, user_id, permission_id)`

### `user_roles`

Assigns roles to users with effective dating, approval state, and source tracking (manual vs generated from a position access rule).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users |
| `role_id` | `uuid` | FK -> roles |
| `source_type` | `varchar(30)` | `Manual`, `PositionTemplate`, or `EmployeeOverride` |
| `source_position_id` | `uuid` | Nullable FK -> positions when generated from a position |
| `source_position_access_template_id` | `uuid` | Nullable FK -> position_access_templates |
| `effective_from` | `timestamptz` | Nullable; defaults to immediate when null |
| `effective_to` | `timestamptz` | Nullable |
| `status` | `varchar(20)` | `Active`, `Scheduled`, `PendingApproval`, `Expired`, or `Revoked` |
| `assigned_at` | `timestamptz` |  |
| `assigned_by` | `uuid` | FK -> users (who granted this) |
| `approved_by` | `uuid` | Nullable FK -> users |
| `expires_at` | `timestamptz` | Deprecated compatibility alias; use `effective_to` |

**Unique:** `(tenant_id, user_id, role_id, source_position_id, effective_from)`

### `access_grant_requests`

Phase 1 lightweight approval records for sensitive position-based access (not a Workflow Engine instance).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `user_id` | `uuid` | FK -> users who will receive the grant |
| `action_type` | `varchar(30)` | `Onboarding`, `Transfer`, `Promotion`, or `PositionAssignment` |
| `target_position_id` | `uuid` | FK -> positions |
| `target_department_id` | `uuid` | FK -> departments; used for approver routing |
| `position_access_template_id` | `uuid` | FK -> position_access_templates |
| `requested_role_id` | `uuid` | FK -> roles |
| `approval_status` | `varchar(20)` | `Pending`, `Approved`, `Rejected`, or `Cancelled` |
| `requested_by` | `uuid` | FK -> users |
| `approved_by` | `uuid` | Nullable FK -> users |
| `requested_at` | `timestamptz` |  |
| `decided_at` | `timestamptz` | Nullable |
| `effective_from` | `timestamptz` | Grant effective start after approval |
| `effective_to` | `timestamptz` | Nullable grant end |
| `decision_comment` | `varchar(500)` | Nullable |

### `refresh_tokens`

Hashed JWT refresh token store with a rotation chain, enabling secure session renewal and token revocation.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `user_id` | `uuid` | FK -> users |
| `token_hash` | `varchar(128)` | SHA-256 hash of token - never store raw |
| `expires_at` | `timestamptz` | 7 days from creation |
| `replaced_by_id` | `uuid` | Self-referencing FK - token rotation chain |
| `revoked_at` | `timestamptz` | Nullable - set when token is revoked |
| `created_at` | `timestamptz` | |

### `user_mfa`

MFA method registrations per user (`totp` primary; `email_otp_fallback` fallback/recovery only).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `user_id` | `uuid` | FK -> users |
| `tenant_id` | `uuid` | FK -> tenants |
| `method` | `varchar(20)` | `totp`, `email_otp_fallback` |
| `secret_encrypted` | `varchar(500)` | Encrypted TOTP secret for `totp`; temporary hashed fallback OTP only for `email_otp_fallback` challenges |
| `is_verified` | `boolean` | User has confirmed setup with a valid code |
| `last_used_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` | Nullable |

**Unique:** `(user_id, method)`

### `mfa_recovery_codes`

One-time-use backup codes, stored as BCrypt hashes.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `user_id` | `uuid` | FK -> users |
| `code_hash` | `varchar(255)` | BCrypt hash of the recovery code |
| `used_at` | `timestamptz` | Nullable - set when the code is consumed |
| `created_at` | `timestamptz` |  |

### `mfa_challenges`

Stores short-lived, server-side MFA login challenge state during browser login after password verification and before MFA verification completes.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users |
| `challenge_hash` | `varchar(128)` | UNIQUE. Hash of the opaque challenge token; never store raw. |
| `failed_attempt_count` | `integer` | Default 0 |
| `expires_at` | `timestamptz` | |
| `consumed_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` | |

**Indexes/Constraints:**
- unique index on `challenge_hash`
- index on `(tenant_id, user_id, expires_at)`
- index on `expires_at`

**Documentation Discrepancy / Decision Log:**
This table was added because `tenant_auth_policies`, `user_mfa`, and `mfa_recovery_codes` do not cover short-lived pending MFA login state. Phase 1 uses browser cookie session auth and MFA can pause login between password success and MFA verification. Process-local in-memory challenge storage is acceptable only for local development/testing, not production/staging. Redis/shared cache is not the chosen Phase 1 approach. PostgreSQL-backed challenge storage is the approved Phase 1 durable approach. Do not use `outbox_messages` for MFA challenge state. Do not use `sessions` for pre-MFA authenticated state. Do not store raw challenge values.

---

## Org Structure (8 tables)

### `legal_entities`

Companies/legal entities inside a tenant (user-facing term: "Company") - the scoping boundary for departments, positions, employees, Time Off policies, payroll context, and timezone.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `parent_legal_entity_id` | `uuid` | FK -> legal_entities, nullable |
| `name` | `varchar(200)` | Company legal name |
| `logo_file_id` | `uuid` | Nullable FK -> file_records |
| `registration_number` | `varchar(50)` | nullable |
| `tax_identifier` | `varchar(80)` | nullable |
| `country_id` | `uuid` | FK -> countries |
| `currency_code` | `varchar(3)` | ISO 4217 currency |
| `address_json` | `jsonb` |  |
| `timezone` | `varchar(50)` | IANA timezone for the Company; primary timezone for schedule interpretation, attendance reconciliation, late rules, overtime, and Time Off conversion |
| `default_language` | `varchar(10)` | e.g., `en` |
| `date_format` | `varchar(20)` | Company display preference |
| `week_start_day` | `smallint` | 1-7, implementation-defined mapping |
| `is_active` | `boolean` |  |
| `created_at` | `timestamptz` |  |

### `departments`

Organizational units within a legal entity, used to group employees and positions and to route approvals and reports.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities |
| `name` | `varchar(100)` | Unique within legal entity |
| `code` | `varchar(20)` | Stable short identifier; unique within legal entity |
| `parent_department_id` | `uuid` | Self-referencing FK (nullable) |
| `head_position_id` | `uuid` | FK -> positions; must be `unique` type; nullable |
| `is_active` | `boolean` |  |
| `created_at` | `timestamptz` |  |

### `positions`

First-class org seats defining the reporting hierarchy; legal-entity-scoped.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities |
| `name` | `varchar(100)` | Position name; unique within legal entity |
| `code` | `varchar(40)` | Stable tenant-unique identifier for import and integrations |
| `position_type` | `varchar(20)` | `unique` or `pooled` |
| `max_occupancy` | `int` | Must be `1` for `unique`; >= `1` for `pooled` |
| `department_id` | `uuid` | FK -> departments |
| `reports_to_position_id` | `uuid` | Current reporting snapshot; self-referencing FK to a same-legal-entity `unique` position, nullable for root positions |
| `is_active` | `boolean` |  |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` | nullable |

### `position_access_templates`

Persistence for the "Grant system access from this position" rules; generates `user_roles` grants or `access_grant_requests` on employee movement.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `position_id` | `uuid` | FK -> positions |
| `role_id` | `uuid` | FK -> roles |
| `requires_approval` | `boolean` | True for sensitive or broad grants that must be confirmed by access authority |
| `is_sensitive` | `boolean` | Marks templates that expose sensitive HR, payroll, security, or broad employee data |
| `effective_from_rule` | `varchar(30)` | `assignment_effective_from` or explicit date rule |
| `effective_to_rule` | `varchar(30)` | Nullable. Usually `assignment_effective_to` |
| `is_active` | `boolean` | Inactive templates are ignored for future assignments |
| `created_by` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` | nullable |

### `management_coverage_records`

Single source for employee visibility and Phase 1 approval routing.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; selected Company context |
| `owner_position_id` | `uuid` | FK -> positions; position that can manage the covered target |
| `covered_target_type` | `varchar(20)` | `Position`, `Department`, or `Company` |
| `covered_position_id` | `uuid` | Nullable FK -> positions; required when target type is `Position` |
| `covered_department_id` | `uuid` | Nullable FK -> departments; required when target type is `Department` |
| `owner_order` | `int` | Internal ordering only: 1 = Primary owner, 2 = Backup owner 1, 3 = Backup owner 2 |
| `source` | `varchar(30)` | `ReportingStructure` or `Manual` |
| `is_locked` | `boolean` | True for generated reporting-structure coverage |
| `status` | `varchar(20)` | `active` or `inactive` |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `position_reporting_history`

Effective-dated reporting relationship history between positions.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `position_id` | `uuid` | FK -> positions |
| `reports_to_position_id` | `uuid` | FK -> positions, nullable for root positions |
| `effective_from` | `date` | Start date for this reporting relationship |
| `effective_to` | `date` | Nullable. Null means current open reporting relationship |
| `created_at` | `timestamptz` |  |

### `position_assignments`

Effective-dated employee placement into positions.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `position_id` | `uuid` | FK -> positions |
| `assignment_kind` | `varchar(30)` | `PrimaryEmployment` or `AdditionalAuthority` |
| `effective_from` | `date` | Start date for this assignment |
| `effective_to` | `date` | Nullable. Null means current open assignment |
| `assignment_status` | `varchar(20)` | `active`, `planned`, `ended`, `cancelled` |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` | nullable |

### `employee_hierarchy_closure`

Current derived reporting tree for fast hierarchy queries (rebuildable; not source of truth).

| Column | Type | Notes |
|:-------|:-----|:------|
| `tenant_id` | `uuid` | FK -> tenants |
| `ancestor_employee_id` | `uuid` | Resolved manager/ancestor employee |
| `descendant_employee_id` | `uuid` | Resolved subordinate employee |
| `depth` | `int` | `1` for direct reports, greater than `1` for indirect reports |
| `source_position_assignment_id` | `uuid` | FK -> position_assignments that produced the descendant placement |
| `generated_at` | `timestamptz` | When this row was generated |

**PK:** `(tenant_id, ancestor_employee_id, descendant_employee_id)`

---

## Core HR (14 tables)

### `employees`

Master employee record - the HR profile behind every person in the system (identity, employment type/status, work mode, key dates). Referenced by 71+ tables.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users (1:1) |
| `employee_number` | `varchar(20)` | Unique per tenant |
| `first_name` | `varchar(100)` |  |
| `last_name` | `varchar(100)` |  |
| `email` | `varchar(255)` | Work email |
| `phone` | `varchar(20)` |  |
| `date_of_birth` | `date` | PII - CONFIDENTIAL |
| `gender` | `varchar(10)` |  |
| `nationality_id` | `uuid` | FK -> countries |
| `department_id` | `uuid` | FK -> departments, nullable; current profile snapshot derived from active position when available |
| `legal_entity_id` | `uuid` | FK -> legal_entities |
| `employment_type` | `varchar(20)` | `full_time`, `part_time`, `contract`, `intern` |
| `employment_status` | `varchar(20)` | `onboarding`, `active`, `on_leave`, `offboarding`, `suspended`, `terminated`, `resigned` |
| `work_mode` | `varchar(10)` | `onsite`, `remote`, `hybrid`, `field` |
| `hire_date` | `date` |  |
| `probation_end_date` | `date` | Nullable |
| `termination_date` | `date` | Nullable |
| `avatar_file_id` | `uuid` | FK -> file_records, nullable |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |
| `is_deleted` | `boolean` | Soft delete |
| `display_timezone` | `varchar(50)` | Nullable. IANA timezone for UI display only; does not change schedule interpretation, attendance rules, or deductions |

### `employee_addresses`

Employee postal addresses (permanent, current, emergency) kept off the main profile row.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `address_type` | `varchar(20)` | `permanent`, `current`, `emergency` |
| `address_json` | `jsonb` | Street, city, state, postal, country |
| `is_primary` | `boolean` |  |

### `employee_bank_details`

Employee bank accounts for salary payment; account numbers stored encrypted (AES-256).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `bank_name` | `varchar(100)` |  |
| `branch_name` | `varchar(100)` |  |
| `account_number_encrypted` | `bytea` | **Encrypted** via `IEncryptionService` (AES-256) |
| `routing_number` | `varchar(20)` |  |
| `is_primary` | `boolean` |  |

### `employee_custom_fields`

Tenant-defined extra fields on the employee profile without schema changes.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `field_name` | `varchar(100)` |  |
| `field_value` | `text` |  |
| `field_type` | `varchar(20)` | `text`, `number`, `date`, `boolean`, `select` |

### `employee_dependents`

Employee family members/dependents for HR records and benefits.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `name` | `varchar(100)` |  |
| `relationship` | `varchar(20)` | `spouse`, `child`, `parent`, `other` |
| `date_of_birth` | `date` |  |
| `is_emergency_contact` | `boolean` |  |
| `phone` | `varchar(20)` |  |

### `employee_emergency_contacts`

Who to contact in an emergency for each employee.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `name` | `varchar(100)` |  |
| `relationship` | `varchar(30)` |  |
| `phone` | `varchar(20)` |  |
| `email` | `varchar(255)` |  |
| `is_primary` | `boolean` |  |

### `employee_lifecycle_events`

Timeline of employment events (hired, promoted, transferred, salary change, terminated...) that builds the employee's history view.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `event_type` | `varchar(30)` | `hired`, `promoted`, `transferred`, `salary_change`, `suspended`, `terminated`, `resigned` |
| `event_date` | `date` |  |
| `details_json` | `jsonb` | Event-specific data |
| `performed_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |

### `employee_work_history`

Previous employment (before joining this company) captured on the employee profile.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `company_name` | `varchar(200)` |  |
| `start_date` | `date` |  |
| `end_date` | `date` |  |
| `reason_for_leaving` | `varchar(255)` |  |

### `employee_assignment_history`

Effective-dated assignment history for department and position snapshots.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `department_id` | `uuid` | FK -> departments, nullable |
| `position_id` | `uuid` | FK -> positions, nullable |
| `effective_from` | `date` | Start date for this assignment |
| `effective_to` | `date` | Nullable. Null means current open assignment |

### `employee_transfers`

Lightweight request record for employee transfer (not a Workflow Engine instance).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `from_department_id` | `uuid` | nullable |
| `to_department_id` | `uuid` | nullable |
| `from_position_id` | `uuid` | nullable |
| `to_position_id` | `uuid` | nullable |
| `effective_date` | `date` | When approved transfer becomes active |
| `status` | `varchar(30)` | `Pending`, `Approved`, `Rejected`, `Cancelled`, `Applied` |
| `reason` | `varchar(500)` | Business reason |
| `requested_by_id` | `uuid` | FK -> users |
| `approved_by_id` | `uuid` | FK -> users, nullable |

### `offboarding_records`

Tracks an employee's exit process - reason, last working day, knowledge-transfer risk, penalties, exit interview, and completion status.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `reason` | `varchar(30)` | `resignation`, `termination`, `retirement`, `contract_end` |
| `last_working_date` | `date` |  |
| `knowledge_risk_level` | `varchar(10)` | `low`, `medium`, `high`, `critical` |
| `exit_interview_notes` | `text` |  |
| `penalties_json` | `jsonb` | Outstanding loans, notice period, asset recovery, knowledge-transfer bypass penalties, etc. |
| `status` | `varchar(20)` | `initiated`, `in_progress`, `completed` |
| `created_at` | `timestamptz` |  |

### `onboarding_drafts`

Pre-invite onboarding records. Drafts hold the pending employee/job/schedule/checklist state before the system creates the employee, user account, invitation token, activated checklist tasks, or policy assignments.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_name` | `varchar(255)` | Pending employee display/full name |
| `work_email` | `varchar(255)` | Work email to use for the eventual invitation |
| `legal_entity_id` | `uuid` | FK -> legal_entities |
| `department_id` | `uuid` | FK -> departments, nullable when derived from position or not yet selected |
| `position_id` | `uuid` | FK -> positions, nullable until assignment is selected |
| `employment_type` | `varchar(30)` | e.g., `full_time`, `part_time`, `contractor`, `intern` |
| `start_date` | `date` | Planned start date |
| `employee_number` | `varchar(20)` | Nullable when auto-generated at final creation |
| `schedule_id` | `uuid` | FK -> work_schedules, nullable |
| `selected_template_id` | `uuid` | FK -> checklist_templates, nullable until checklist selection |
| `edited_tasks_json` | `jsonb` | Editable task set copied from template and modified for this pending employee |
| `status` | `varchar(30)` | `draft`, `waiting_for_seat`, `waiting_for_position_approval`, `cancelled`, `finalized` |
| `draft_reason` | `varchar(50)` | `saved_manually`, `waiting_for_seat`, `waiting_for_position_approval`, nullable |
| `last_saved_step` | `varchar(50)` | Last completed UI/workflow step for resume |
| `started_by_id` | `uuid` | FK -> users; only the creator's own drafts are shown in My Drafts |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |
| `finalized_at` | `timestamptz` | Nullable timestamp when final employee creation succeeds |

### `bulk_onboarding_batches`

One CSV upload's worth of prospective employees, from upload through validation, background
draft creation, and background finalize. Column mapping is ephemeral (this-batch-only, never
reused across uploads).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; batch-level default for every row |
| `default_employment_type` | `varchar(30)` | Nullable; batch-level default, CSV column can override per row |
| `default_work_mode_id` | `int` | Nullable, FK -> work_modes; batch-level default, CSV column can override per row |
| `default_checklist_template_id` | `uuid` | Nullable, FK -> checklist_templates; batch-level default, CSV column can override per row |
| `column_mapping` | `jsonb` | System field -> CSV header map; ephemeral to this batch |
| `selected_draft_ids` | `jsonb` | Nullable; onboarding_draft ids selected at finalize time |
| `original_file_name` | `varchar(255)` | Display only |
| `status` | `varchar(30)` | `mapping_pending`, `validated`, `draft_creation_pending`, `drafts_created`, `finalize_pending`, `finalize_completed` |
| `total_rows` | `int` | |
| `valid_rows` | `int` | Nullable until validated |
| `invalid_rows` | `int` | Nullable until validated |
| `created_by_user_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |
| `completed_at` | `timestamptz` | Nullable; set when finalize_completed |

### `bulk_onboarding_batch_rows`

One CSV row's parsed data, resolution, and lifecycle status within a `bulk_onboarding_batches`
batch. Bulk-created drafts are ordinary `onboarding_drafts` rows (see that table) linked back
here by `onboarding_draft_id` - they also appear in the normal My Drafts list.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | |
| `batch_id` | `uuid` | FK -> bulk_onboarding_batches |
| `row_number` | `int` | 1-based, matches the CSV row for error reporting |
| `raw_data` | `jsonb` | Original cell values keyed by detected CSV header |
| `resolved_department_id` | `uuid` | Nullable; resolved at validation time by department name |
| `resolved_position_id` | `uuid` | Nullable; resolved at validation time by position name |
| `resolved_template_id` | `uuid` | Nullable; resolved checklist template, row override of the batch default |
| `status` | `varchar(30)` | `pending_mapping`, `valid`, `invalid`, `draft_created`, `draft_failed`, `finalized`, `waiting_for_seat`, `waiting_for_position_approval`, `finalize_failed` |
| `error_message` | `text` | Nullable |
| `onboarding_draft_id` | `uuid` | Nullable FK -> onboarding_drafts |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Unique:** `(tenant_id, batch_id, row_number)`.

### `employee_checklist_tasks`

Individual onboarding/offboarding checklist tasks instantiated for a specific employee, with owner and due date.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `employee_id` | `uuid` | FK -> employees |
| `template_id` | `uuid` | FK -> checklist_templates, nullable for manual task |
| `lifecycle_type` | `varchar(20)` | `onboarding` or `offboarding` |
| `task_title` | `varchar(200)` |  |
| `owner_type` | `varchar(30)` | `employee`, `manager`, `hr`, `it`, `custom_user` |
| `sequence` | `int` | Nullable display/order value |
| `assigned_to_id` | `uuid` | FK -> users |
| `due_date` | `date` |  |
| `status` | `varchar(20)` | `pending`, `in_progress`, `completed` |
| `completed_at` | `timestamptz` |  |

### `checklist_templates`

Reusable onboarding/offboarding checklist definitions that get instantiated as `employee_checklist_tasks` for new joiners and leavers.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` |  |
| `name` | `varchar(100)` |  |
| `template_type` | `varchar(20)` | `onboarding` or `offboarding` |
| `department_id` | `uuid` | FK -> departments (nullable - global template) |
| `tasks_json` | `jsonb` | Task definitions: title, owner type, due rule, and sequence |
| `is_active` | `boolean` |  |

---

## Time Off (7 tables)

> Canonical balance unit: **minutes** (integer) for all entitlement, used, available, carry-forward, adjustment, request duration, and deduction fields.

### `time_off_types`

Tenant catalog of leave types (Annual, Sick, Maternity...) and whether each is paid or needs a supporting document.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(50)` | e.g., Annual, Sick, Maternity |
| `code` | `varchar(30)` | Unique per tenant where implemented |
| `description` | `text` | Nullable |
| `is_paid` | `boolean` | Type-level classification if supported |
| `requires_document` | `boolean` | Whether supporting document is required |
| `is_active` | `boolean` | |
| `created_at` | `timestamptz` | |

### `time_off_policies`

Policy header per Company with effective dating; per-type rules and assignment scope hang off it as child tables.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; derived from the selected Company context in the topbar |
| `name` | `varchar(100)` | |
| `country_id` | `uuid` | Nullable statutory context |
| `is_active` | `boolean` | Active within the selected Company context |
| `effective_from` | `date` | |
| `effective_to` | `date` | Nullable; closes the old policy when a replacement starts |
| `created_at` | `timestamptz` | |

### `time_off_policy_rules`

Per-time-off-type entitlement, accrual, carry-forward, and request limits inside a policy (all amounts in minutes).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | |
| `policy_id` | `uuid` | FK -> time_off_policies |
| `time_off_type_id` | `uuid` | FK -> time_off_types |
| `entitlement_minutes` | `int` | Annual entitlement in minutes. Admin enters hours/minutes in UI |
| `accrual_method` | `varchar(20)` | `yearly`, `monthly`, `prorated` as supported |
| `proration_method` | `varchar(20)` | `calendar_days`, `working_days` as supported |
| `carry_forward_allowed` | `boolean` | |
| `carry_forward_limit_minutes` | `int` | Nullable |
| `carry_forward_expiry` | `varchar(50)` | Nullable policy period/month/date expression |
| `rollover_period` | `varchar(20)` | `monthly`, `yearly`, or `policy_period` |
| `minimum_request_minutes` | `int` | Nullable |
| `max_consecutive_minutes` | `int` | Nullable; unlimited when null |
| `notice_period_days` | `int` | Nullable |
| `created_at` | `timestamptz` | |

### `time_off_policy_assignments`

Date-effective scope of a policy: company default, department, position, or employee override (most specific wins at resolution).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | |
| `policy_id` | `uuid` | FK -> time_off_policies |
| `scope_type` | `varchar(30)` | `legal_entity_default`, `department`, `position`, or `employee_override` |
| `scope_id` | `uuid` | Nullable only for `legal_entity_default` |
| `effective_from` | `date` | Date-effective assignment start |
| `effective_to` | `date` | Nullable |
| `created_at` | `timestamptz` | |

### `time_off_entitlements`

Materialized per-employee, per-type, per-period leave balance (entitled/used/pending/carried-forward/available, all in minutes).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | |
| `legal_entity_id` | `uuid` | FK -> legal_entities |
| `employee_id` | `uuid` | FK -> employees |
| `time_off_type_id` | `uuid` | FK -> time_off_types |
| `period_year` | `int` | Or policy period key |
| `policy_id` | `uuid` | FK -> time_off_policies |
| `entitlement_minutes` | `int` | Canonical entitlement balance in minutes |
| `used_minutes` | `int` | Updated on approval or late deduction |
| `pending_minutes` | `int` | Pending requests when stored |
| `carried_forward_minutes` | `int` | From previous policy period |
| `available_minutes` | `int` | Computed or stored as implemented |

### `time_off_requests`

Employee leave requests and their approval lifecycle, storing the canonical requested duration and the deduction captured at approval time.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | |
| `employee_id` | `uuid` | FK -> employees |
| `time_off_type_id` | `uuid` | FK -> time_off_types |
| `start_date` | `date` | |
| `end_date` | `date` | |
| `start_time` | `time` | Nullable; used when the user specifies exact start time |
| `end_time` | `time` | Nullable; used when the user specifies exact end time |
| `request_duration_minutes` | `int` | Required; canonical requested Time Off duration in minutes |
| `deduction_minutes` | `int` | Actual approved deduction in minutes; captured at approval time |
| `reason` | `text` | |
| `status` | `varchar(20)` | `pending`, `approved`, `rejected`, `cancelled` |
| `assigned_approver_id` | `uuid` | Nullable FK -> users; required while a normally routed request is pending |
| `approved_by_id` | `uuid` | FK -> users |
| `approved_at` | `timestamptz` | |
| `rejected_by_id` | `uuid` | Nullable FK -> users |
| `rejected_at` | `timestamptz` | Nullable |
| `rejection_reason` | `text` | Nullable; required when status becomes `rejected` |
| `cancellation_type` | `varchar(20)` | Nullable; `requester` or `expired` |
| `cancelled_by_user_id` | `uuid` | Nullable FK -> users; requester for manual cancellation, null for expiration |
| `cancellation_reason` | `text` | Nullable; required when status becomes `cancelled` |
| `cancelled_at` | `timestamptz` | Nullable |
| `conflict_snapshot_json` | `jsonb` | Calendar conflicts at submission time |
| `document_file_id` | `uuid` | FK -> file_records, nullable |
| `created_at` | `timestamptz` | |

Phase 1 stores the single assigned approval owner and terminal decision directly on `time_off_requests`. Inbox presentation uses actionable `notifications`; no generic approval/action or Inbox-item table is created. Request-more-information conversations are deferred to Phase 2 Workflow/Chat case conversations.

**Cancellation rules:** Only the employee who owns the request may manually cancel it. A pending request may be cancelled by its requester. An approved request may be cancelled by its requester only before the leave starts; the captured deduction is restored once in the same transaction. A recurring application job automatically cancels a request that is still pending after its complete requested period has elapsed in the applicable Company/legal-entity timezone. Automatic expiration uses `cancellation_type = 'expired'` and `cancelled_by_user_id = null`.

### `time_off_balances_audit`

Immutable ledger of every balance mutation (accrual, deduction, carry-forward, forfeiture, adjustment, late deduction) - the audit trail behind every entitlement number.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | |
| `employee_id` | `uuid` | FK -> employees |
| `time_off_type_id` | `uuid` | FK -> time_off_types |
| `policy_id` | `uuid` | FK -> time_off_policies, nullable for manual adjustments |
| `entitlement_id` | `uuid` | FK -> time_off_entitlements, nullable |
| `time_off_request_id` | `uuid` | FK -> time_off_requests, nullable |
| `attendance_record_id` | `uuid` | FK -> attendance_records, nullable; populated for late-arrival deductions |
| `change_type` | `varchar(20)` | `accrual`, `deduction`, `carry_forward`, `forfeiture`, `adjustment`, `late_deduction` |
| `minutes_changed` | `int` | Positive or negative |
| `balance_after_minutes` | `int` | Balance after mutation |
| `source` | `varchar(30)` | `time_off`, `time_attendance`, `manual`, `system` |
| `calculation_snapshot_json` | `jsonb` | Nullable; stores bracket calculation details for late deductions |
| `reason` | `varchar(255)` | |
| `created_by_id` | `uuid` | FK -> users, nullable for system jobs |
| `created_at` | `timestamptz` | |

---

## Calendar (5 tables)

### `calendar_events`

Unified calendar event store - manual events plus projected time-off/holiday/review events and events synced from Google/Outlook.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `title` | `varchar(200)` | |
| `description` | `text` | Nullable |
| `start_date` | `timestamptz` | |
| `end_date` | `timestamptz` | |
| `source_type` | `varchar(30)` | `manual`, `time_off_request`, `holiday`, `external_sync`, `schedule_overlay`. Task due-date chips and worked-time blocks are projected at read-time, not stored |
| `source_id` | `uuid` | Polymorphic reference |
| `color` | `varchar(7)` | Nullable hex color |
| `recurrence` | `varchar(20)` | `none`, `daily`, `weekly`, `monthly` |
| `external_id` | `varchar(255)` | Nullable external system event ID, used for deduplication |
| `external_source` | `varchar(30)` | Nullable: `google_calendar`, `outlook_calendar`, `country_holiday` |
| `is_all_day` | `boolean` | Default false; true for all-day events |
| `timezone` | `varchar(50)` | IANA timezone; nullable for all-day events |
| `event_status` | `varchar(20)` | `confirmed`, `tentative`, `cancelled`; nullable for manual events |
| `is_private` | `boolean` | Default false; true for private external events displayed as "Busy" |
| `organizer_name` | `varchar(200)` | Nullable; from external provider |
| `organizer_email` | `varchar(255)` | Nullable; from external provider |
| `location` | `varchar(500)` | Nullable; location text from external provider |
| `meeting_link` | `varchar(500)` | Nullable; meeting URL from external provider |
| `external_attendees` | `jsonb` | Nullable; attendee list from external provider: `[{name, email, status}]` |
| `recurrence_rule` | `text` | Nullable; RRULE string from external provider |
| `external_updated_at` | `timestamptz` | Nullable; last modified timestamp from provider |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `calendar_event_participants`

Employee invitees per calendar event with their accept/reject response state.

| Column | Type | Notes |
|:-------|:-----|:------|
| `event_id` | `uuid` | FK -> calendar_events |
| `employee_id` | `uuid` | FK -> employees |
| `response_status` | `varchar(30)` | `pending`, `accepted`, `rejected`, `resolution_requested`, `replacement_nominated` when supported |
| `response_reason` | `text` | Nullable; required for rejection |

### `holiday_calendar_settings`

Per-legal-entity country holiday calendar settings for Calendar display.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities |
| `default_country_code` | `char(2)` | ISO 3166-1 alpha-2 from the legal entity country |
| `override_country_code` | `char(2)` | Nullable; admin-selected calendar country override |
| `effective_country_code` | `char(2)` | Derived from override_country_code or default_country_code |
| `holiday_sync_enabled` | `boolean` | Admin can stop country holiday sync from Calendar screen |
| `provider` | `varchar(30)` | Phase 1 default: `nager_date`; fallback: `manual` |
| `last_synced_year` | `integer` | Nullable |
| `last_synced_at` | `timestamptz` | Nullable |
| `updated_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Unique:** `(tenant_id, legal_entity_id)`

### `external_calendar_connections`

User-level Google Calendar / Outlook Calendar OAuth connections; tokens encrypted.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `user_id` | `uuid` | FK -> users |
| `provider` | `varchar(30)` | `google_calendar`, `outlook_calendar` |
| `external_account_email` | `varchar(255)` | Connected Google/Microsoft account email |
| `external_calendar_id` | `varchar(255)` | Calendar ID selected for sync; nullable means primary/default |
| `external_calendar_name` | `varchar(255)` | Display name of the selected external calendar |
| `access_token_encrypted` | `bytea` | Nullable; short-lived |
| `refresh_token_encrypted` | `bytea` | Encrypted refresh token |
| `scopes` | `jsonb` | Granted scopes |
| `sync_direction` | `varchar(20)` | `pull_only`, `push_only`, `two_way`, `disabled` |
| `status` | `varchar(20)` | `active`, `reauth_required`, `paused`, `revoked`, `failed` |
| `sync_token_encrypted` | `bytea` | Nullable encrypted Google Calendar `syncToken` for incremental fetch |
| `delta_link_encrypted` | `bytea` | Nullable encrypted Microsoft Graph delta link/token for incremental fetch |
| `failure_count` | `integer` | Consecutive sync failures; reset to 0 after successful sync |
| `last_synced_at` | `timestamptz` | Nullable |
| `last_successful_sync_at` | `timestamptz` | Nullable |
| `last_error` | `text` | Nullable last provider/sync error |
| `expires_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Unique:** `(tenant_id, user_id, provider, external_calendar_id)`

### `external_calendar_event_links`

Idempotency and sync state for events pulled from or pushed to Google/Outlook.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `calendar_event_id` | `uuid` | FK -> calendar_events |
| `external_calendar_connection_id` | `uuid` | FK -> external_calendar_connections |
| `provider` | `varchar(30)` | `google_calendar`, `outlook_calendar` |
| `external_calendar_id` | `varchar(255)` | Provider calendar ID |
| `external_event_id` | `varchar(255)` | Provider event ID |
| `external_etag` | `varchar(255)` | Provider version/etag for conflict detection |
| `sync_direction` | `varchar(20)` | `inbound`, `outbound` |
| `sync_status` | `varchar(20)` | `synced`, `pending`, `failed`, `skipped`, `conflict` |
| `last_synced_at` | `timestamptz` | Nullable |
| `last_error` | `text` | Nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Unique:** `(tenant_id, provider, external_calendar_id, external_event_id)`

---

## Configuration (11 tables)

> `integration_connections` excluded (Phase 2 only per `configuration.md`). `monitoring_alert_policy` is included in Phase 1 and indexed in the schema catalog.

### `tenant_settings`

Tenant-wide operational defaults: timezone, work week, work hours, privacy mode, and data retention settings.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants, UNIQUE |
| `timezone` | `varchar(50)` | Default timezone |
| `date_format` | `varchar(20)` |  |
| `currency_code` | `varchar(3)` |  |
| `work_week_days_json` | `jsonb` | e.g., `[1,2,3,4,5]` |
| `work_hours_start` | `time` |  |
| `work_hours_end` | `time` |  |
| `privacy_mode` | `varchar(20)` | `full_transparency`, `partial`, `covert` |
| `data_retention_days_json` | `jsonb` | Per-data-type retention settings |
| `settings_json` | `jsonb` | Extensible settings |
| `updated_at` | `timestamptz` |  |

### `monitoring_feature_toggles`

Tenant-level master switches for each monitoring capability; the top of the toggle-inheritance chain that scope and employee overrides refine.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants, UNIQUE |
| `activity_monitoring` | `boolean` | Keyboard/mouse event counting |
| `application_tracking` | `boolean` | App usage tracking |
| `document_tracking` | `boolean` | Document tool time tracking |
| `communication_tracking` | `boolean` | Communication tool active time and send counts |
| `screenshot_capture` | `boolean` | Allows authorized on-demand screenshot capture |
| `auto_screenshot_capture` | `boolean` | Allows automatic screenshot capture on detected deviation |
| `meeting_detection` | `boolean` | Meeting time tracking |
| `device_tracking` | `boolean` | Device usage tracking |
| `work_location_verification` | `boolean` | Network-based work-location compliance |
| `identity_verification` | `boolean` | Photo verification |
| `biometric` | `boolean` | Biometric/attendance terminals |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `monitoring_alert_policy`

Tenant-level Monitoring Policy controlling how monitoring alert recipients are resolved. One row per tenant.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants, UNIQUE |
| `monitoring_alert_recipient_resolver` | `varchar(50)` | `management_coverage_availability_chain` (default) or `reporting_manager` |
| `monitoring_alert_wait_for_scheduled_recipient_grace_minutes` | `int` | Minutes to wait for a scheduled recipient before skipping (default: 15) |
| `monitoring_alert_fallback_to_management_coverage_chain` | `boolean` | Fallback when `reporting_manager` resolver's manager is unavailable (default: true) |
| `monitoring_alert_unresolved_routing_action` | `varchar(30)` | `create_routing_issue` (default) or `leave_unassigned` |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `app_allowlists`

Which applications are allowed or not allowed during work time, per tenant/role/employee scope; the ingest processor matches against `process_name` to flag violations.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `scope_type` | `varchar(20)` | `tenant`, `role`, `employee` |
| `scope_id` | `uuid` | Null for tenant, role_id for role, employee_id for employee |
| `application_name` | `varchar(200)` | e.g., "Microsoft Teams", "Visual Studio Code" |
| `process_name` | `varchar(100)` | e.g., "ms-teams.exe" - authoritative matching key |
| `category` | `varchar(50)` | `browser`, `communication`, `development`, `office`, `design`, `productivity`, `other` |
| `is_allowed` | `boolean` | True = allowed during work, False = not allowed |
| `source` | `varchar(20)` | `global_catalog`, `tenant_observed`, `manual` |
| `global_catalog_id` | `uuid` | Nullable FK -> global_app_catalog (if sourced from catalog) |
| `set_by_id` | `uuid` | FK -> users (who configured this) |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

**Unique:** `(tenant_id, scope_type, COALESCE(scope_id, uuid_nil), process_name)`

### `app_allowlist_audit`

Change history (create/update/delete with before/after snapshots) for allowlist entries.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `allowlist_id` | `uuid` | FK -> app_allowlists |
| `action` | `varchar(20)` | `created`, `updated`, `deleted` |
| `changed_by_id` | `uuid` | FK -> users |
| `old_value_json` | `jsonb` | Previous state |
| `new_value_json` | `jsonb` | New state |
| `changed_at` | `timestamptz` |  |

### `observed_applications`

Auto-populated by the ingest processor whenever an app is seen on an employee device.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `application_name` | `varchar(200)` | Display name reported by agent (e.g., "Google Chrome") |
| `process_name` | `varchar(100)` | Windows exe name (e.g., "chrome.exe") - deduplication key |
| `global_catalog_id` | `uuid` | Auto-linked FK -> global_app_catalog when process_name matches (nullable) |
| `first_seen_at` | `timestamptz` | When this app was first detected for this tenant |
| `last_seen_at` | `timestamptz` | Updated on every ingest that contains this app |
| `employee_count` | `int` | Unique employees who ran this app |
| `total_seconds_observed` | `bigint` | Cumulative usage time across all employees |
| `status` | `varchar(20)` | `pending` / `added_to_allowlist` / `dismissed` |

**Unique:** `(tenant_id, process_name)`

### `employee_monitoring_overrides`

Per-employee exceptions to the tenant monitoring toggles (null column = inherit), with a required reason for the difference.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `activity_monitoring` | `boolean` | Nullable - null means inherit from tenant |
| `application_tracking` | `boolean` | Nullable |
| `document_tracking` | `boolean` | Nullable |
| `communication_tracking` | `boolean` | Nullable |
| `screenshot_capture` | `boolean` | Nullable; allows authorized on-demand screenshot capture |
| `auto_screenshot_capture` | `boolean` | Nullable; automatic capture on detected deviation |
| `meeting_detection` | `boolean` | Nullable |
| `device_tracking` | `boolean` | Nullable |
| `work_location_verification` | `boolean` | Nullable |
| `identity_verification` | `boolean` | Nullable |
| `biometric` | `boolean` | Nullable |
| `override_reason` | `varchar(255)` | Why this employee is different |
| `set_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `monitoring_policy_overrides`

Role/position/department-scoped exceptions to the tenant monitoring toggles, sitting between tenant defaults and employee overrides.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `scope_type` | `varchar(30)` | `role`, `position`, `department` |
| `scope_id` | `uuid` | FK to the corresponding scope table; validated by application logic |
| `activity_monitoring` | `boolean` | Nullable - null means inherit |
| `application_tracking` | `boolean` | Nullable |
| `document_tracking` | `boolean` | Nullable |
| `communication_tracking` | `boolean` | Nullable |
| `screenshot_capture` | `boolean` | Nullable; on-demand screenshot capture |
| `auto_screenshot_capture` | `boolean` | Nullable; automatic capture on detected deviation |
| `meeting_detection` | `boolean` | Nullable |
| `device_tracking` | `boolean` | Nullable |
| `work_location_verification` | `boolean` | Nullable |
| `identity_verification` | `boolean` | Nullable |
| `biometric` | `boolean` | Nullable |
| `override_reason` | `varchar(255)` | Why this scope differs from tenant default |
| `set_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

**Unique:** `(tenant_id, scope_type, scope_id)`

### `employee_work_location_settings`

Resolved per-employee work mode and work-location-verification behavior (grace period, photo challenge on mismatch).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `work_mode` | `varchar(30)` | `onsite`, `remote`, `hybrid`, `field` |
| `work_location_verification_enabled` | `boolean` | Employee-level resolved setting |
| `grace_period_minutes` | `int` | Nullable override |
| `photo_challenge_on_mismatch` | `boolean` | Default true when enforcement is enabled |
| `set_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `employee_remote_work_profiles`

Approved remote work location profile captured from the employee's first approved remote clock-in.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `status` | `varchar(20)` | `pending_capture`, `active`, `archived`, `rejected` |
| `captured_at` | `timestamptz` | When profile was captured |
| `public_ip` | `inet` | Nullable; IP may change by ISP |
| `wifi_ssid` | `varchar(255)` | Nullable |
| `wifi_bssid_hash` | `varchar(100)` | Nullable |
| `gateway_mac_hash` | `varchar(100)` | Nullable |
| `vpn_detected` | `boolean` | Default false |
| `coarse_location_json` | `jsonb` | Nullable; permission-based only |
| `verification_record_id` | `uuid` | FK -> verification_records |
| `approved_by_id` | `uuid` | Nullable FK -> users |
| `created_at` | `timestamptz` |  |
| `archived_at` | `timestamptz` | Nullable |

**Unique:** `(tenant_id, employee_id) WHERE status = 'active'`

### `remote_work_location_change_requests`

Employee requests to replace their approved remote work location profile, routed to one eligible coverage owner for approval.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `current_profile_id` | `uuid` | Nullable FK -> employee_remote_work_profiles |
| `reason` | `text` | Employee-provided reason |
| `status` | `varchar(20)` | `pending`, `approved`, `rejected`, `captured`, `expired` |
| `requested_at` | `timestamptz` |  |
| `reviewed_by_id` | `uuid` | Nullable FK -> users |
| `reviewed_at` | `timestamptz` | Nullable |
| `review_comment` | `text` | Nullable |
| `new_profile_id` | `uuid` | Nullable FK -> employee_remote_work_profiles |

---

# Pillar 2 - Monitoring

> Exception Engine tables are excluded - `exception-engine.md` is marked **Phase 2** (Phase 1 alerts route through Notifications).

## Activity Monitoring (8 tables)

### `activity_snapshots` (append-only)

Fine-grained agent activity samples - keyboard/mouse event counts (never content), active/idle seconds, and foreground app per capture interval.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `captured_at` | `timestamptz` | When agent captured this snapshot |
| `keyboard_events_count` | `int` | Key press count (NOT keystrokes content) |
| `mouse_events_count` | `int` | Mouse event count |
| `active_seconds` | `int` | Seconds with input activity |
| `idle_seconds` | `int` | Seconds without input |
| `intensity_score` | `decimal(5,2)` | 0-100 computed score |
| `foreground_process_name` | `varchar(100)` | Foreground process name (e.g., `code.exe`) |
| `created_at` | `timestamptz` |  |

### `activity_raw_buffer` (append-only)

Landing zone for raw agent payloads as received, kept for reprocessing and debugging before normalization into the typed activity tables.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `agent_device_id` | `uuid` | FK -> registered_agents |
| `received_at` | `timestamptz` | Server receive time |
| `payload_json` | `jsonb` | Raw agent payload |

### `activity_daily_summary`

Pre-aggregated per-employee daily activity rollup (active/idle/meeting time, app classification, focus time, scores) that dashboards and the Discrepancy Engine read instead of raw snapshots.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `date` | `date` |  |
| `total_active_minutes` | `int` |  |
| `total_idle_minutes` | `int` |  |
| `total_meeting_minutes` | `int` |  |
| `active_percentage` | `decimal(5,2)` | Activity rate; not final productivity |
| `productive_app_minutes` | `int` | Active time in work-classified applications |
| `personal_app_minutes` | `int` | Active time in personal-classified applications |
| `unknown_app_minutes` | `int` | Active time where application classification is unknown |
| `focus_minutes` | `int` | Time in 30+ minute uninterrupted productive sessions |
| `activity_score` | `decimal(5,2)` | Monitoring-derived score, 0-100 |
| `data_coverage_percentage` | `decimal(5,2)` | How complete agent/presence data is for the day |
| `top_apps_json` | `jsonb` | Top 5 apps with time |
| `intensity_avg` | `decimal(5,2)` | Average intensity score |
| `keyboard_total` | `int` | Total keyboard events |
| `mouse_total` | `int` | Total mouse events |
| `document_time_minutes` | `int` | Time in locally detectable document applications |
| `deep_focus_sessions_count` | `int` | Count of 30+ min uninterrupted sessions in one app |
| `data_source` | `varchar(20)` | Phase 1: `agent_windows`; `agent_mac` and `ide` reserved for Phase 2 sources |

### `application_categories`

Tenant pattern rules that classify applications into categories and productive/non-productive for usage reporting.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `application_name_pattern` | `varchar(255)` | Glob pattern (e.g., `*chrome*`) |
| `category` | `varchar(100)` | e.g., "Browser", "IDE", "Communication" |
| `is_productive` | `boolean` | Nullable |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |

### `application_usage` (append-only)

Per-application time usage per employee per day (window titles stored only as SHA-256 hashes), including allowlist verdicts.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `date` | `date` |  |
| `application_name` | `varchar(255)` | e.g., "Google Chrome" |
| `process_name` | `varchar(100)` | e.g., `chrome.exe` - authoritative matching key |
| `application_category` | `varchar(100)` | FK-like to `application_categories` |
| `window_title_hash` | `varchar(64)` | SHA-256 hash (privacy - never store raw title) |
| `total_seconds` | `int` | Time spent |
| `is_productive` | `boolean` | Nullable - from `application_categories` |
| `is_allowed` | `boolean` | Nullable - from resolved app allowlist. `false` = violation logged |
| `app_category_type` | `varchar(20)` | `productive`, `communication`, `meeting`, `personal`, `unknown` |

### `device_tracking`

Daily laptop vs estimated-mobile usage split per employee, derived from gap analysis.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `date` | `date` |  |
| `laptop_active_minutes` | `int` |  |
| `estimated_mobile_minutes` | `int` | Estimated from gap analysis |
| `laptop_percentage` | `decimal(5,2)` |  |
| `detection_method` | `varchar(30)` | `agent`, `manual` |

### `meeting_sessions`

Detected meeting sessions per employee (platform, duration, camera/mic activity) so meeting time is separated from idle time.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `meeting_start` | `timestamptz` |  |
| `meeting_end` | `timestamptz` |  |
| `platform` | `varchar(20)` | Phase 1 process-detectable values: `teams`, `zoom`, `other` |
| `duration_minutes` | `int` | Computed |
| `had_camera_on` | `boolean` | Detected via process inspection |
| `had_mic_activity` | `boolean` | Detected via audio device usage |

### `monitoring_evidence_assets`

Evidence files captured by the monitoring agent (never stored in `entity_assets`).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `agent_device_id` | `uuid` | Nullable FK -> registered_agents |
| `activity_snapshot_id` | `uuid` | Nullable FK -> activity_snapshots |
| `activity_event_id` | `uuid` | Nullable source event ID |
| `captured_at` | `timestamptz` |  |
| `file_record_id` | `uuid` | FK -> file_records (blob storage) |
| `evidence_type` | `varchar(40)` | `screenshot`, `app_snapshot`, `idle_evidence` |
| `source` | `varchar(30)` | `agent`, `system` |
| `trigger_type` | `varchar(20)` | `on_demand`, `auto_deviation` |
| `retention_policy_id` | `uuid` | Nullable FK -> retention_policies |
| `legal_hold_id` | `uuid` | Nullable FK -> legal_holds |
| `metadata` | `jsonb` | Safe non-secret metadata |
| `created_at` | `timestamptz` |  |

## Discrepancy Engine (3 tables)

### `discrepancy_events`

Daily discrepancy detection results - HR active time vs Work Management task time vs calendar-explained time. Written by `DiscrepancyEngineJob` (daily 10:30 PM).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `date` | `date` | |
| `hr_active_minutes` | `int` | Ground truth from `activity_daily_summary` |
| `work_management_logged_minutes` | `int` | Aggregated from `work_management_daily_time_logs.total_logged_minutes` |
| `calendar_minutes` | `int` | Explained time from actual `calendar_events` rows |
| `unaccounted_minutes` | `int` | Computed: `hr_active - work_management_logged - calendar`. Negative = under-reporter |
| `severity` | `varchar(20)` | `none`, `low`, `high`, `critical` - based on tenant threshold config |
| `threshold_minutes` | `int` | Tenant-configured acceptable gap (default 60 min) |
| `notified_owner` | `boolean` | Whether the assigned reviewer or coverage owner was alerted |
| `notified_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` | |
| `z_score` | `decimal(8,2)` | Nullable. How many stddevs above baseline |
| `baseline_avg_minutes` | `decimal(8,2)` | Nullable. Employee's 30-day avg at time of computation |
| `baseline_stddev_minutes` | `decimal(8,2)` | Nullable. Employee's 30-day stddev at time of computation |
| `severity_method` | `varchar(20)` | `absolute` (new employee, < 5 samples) or `baseline_relative` |

### `work_management_daily_time_logs`

Work Management-submitted task time per employee per day; upserted (re-submission for same employee + date overwrites).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `date` | `date` | |
| `total_logged_minutes` | `int` | Aggregated from all task log entries for this day |
| `active_task_at` | `timestamptz` | Most recent active task timestamp (nullable - real-time context) |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Unique:** `(tenant_id, employee_id, date)`

### `employee_discrepancy_baselines`

Rolling per-employee statistical baseline for discrepancy severity; computed daily; needs >= 5 samples.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `computed_at` | `date` | The date this baseline was computed for |
| `window_days` | `int` | Rolling window (default 30) |
| `avg_unaccounted_minutes` | `decimal(8,2)` | Rolling average of unaccounted gap |
| `stddev_unaccounted_minutes` | `decimal(8,2)` | Rolling stddev of unaccounted gap |
| `sample_count` | `int` | Days with data in the window (< 5 -> not used) |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Unique:** `(tenant_id, employee_id, computed_at)`

---

## Time & Attendance (18 tables)

### `presence_sessions`

Daily presence roll-up per employee combining all sources (agent, biometric, manual) - first/last seen, present minutes, break minutes.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `date` | `date` | The work day |
| `first_seen_at` | `timestamptz` | First sign of presence (any source) |
| `last_seen_at` | `timestamptz` | Last sign of presence |
| `total_present_minutes` | `int` | Computed from all sources |
| `total_break_minutes` | `int` | Sum of break records |
| `source` | `varchar(20)` | `biometric`, `agent`, `manual`, `mixed` |
| `status` | `varchar(20)` | `present`, `absent`, `partial`, `on_leave` |
| `created_at` | `timestamptz` | Audit |
| `updated_at` | `timestamptz` | Audit |

### `attendance_records`

Daily attendance summary - one row per employee per work day; stores schedule-expected values and clock-in/out actuals.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `date` | `date` | The work day |
| `work_schedule_id` | `uuid` | Nullable FK -> work_schedules; resolved schedule for this date |
| `expected_working_day` | `boolean` | True if schedule defines this as a working day |
| `work_time_type` | `varchar(20)` | `fixed` or `flexible`; null if not a working day |
| `scheduled_start` | `time` | Nullable; from resolved schedule when work_time_type = fixed |
| `scheduled_end` | `time` | Nullable; from resolved schedule when work_time_type = fixed |
| `required_work_minutes` | `int` | Nullable; from resolved schedule when work_time_type = flexible |
| `expected_work_area` | `varchar(10)` | `onsite`, `remote`, `either`, `field` |
| `schedule_timezone` | `varchar(50)` | IANA timezone from resolved schedule |
| `is_holiday` | `boolean` | True if date is a schedule holiday |
| `holiday_name` | `varchar(100)` | Nullable; holiday name if is_holiday = true |
| `actual_start` | `timestamptz` | Nullable; first check-in (biometric, agent, web, manual) |
| `actual_end` | `timestamptz` | Nullable; last check-out |
| `worked_minutes` | `int` | Total clocked time minus breaks |
| `break_minutes` | `int` | Total break time in minutes |
| `late_minutes` | `int` | Nullable; minutes late vs scheduled start on fixed days |
| `short_minutes` | `int` | Nullable; minutes below required_work_minutes on flexible days |
| `detected_work_area` | `varchar(10)` | Nullable; `onsite`, `remote`, `field` - detected from evidence |
| `attendance_source` | `varchar(20)` | `biometric`, `agent`, `web`, `manual`, `mixed` |
| `status` | `varchar(30)` | `on_time`, `late`, `short_hours`, `absent`, `work_area_mismatch`, `on_time_off`, `holiday`, `off_day` |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `attendance_corrections`

Corrections to attendance facts after the fact (clock-in, clock-out, break, full day); approval flow when required by policy.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `legal_entity_id` | `uuid` | FK -> legal_entities |
| `presence_session_id` | `uuid` | Nullable FK -> presence_sessions |
| `attendance_record_id` | `uuid` | Nullable FK -> attendance_records |
| `correction_type` | `varchar(30)` | `clock_in`, `clock_out`, `break`, `full_day`, `other` |
| `original_clock_in_at` | `timestamptz` | Nullable |
| `original_clock_out_at` | `timestamptz` | Nullable |
| `requested_clock_in_at` | `timestamptz` | Nullable |
| `requested_clock_out_at` | `timestamptz` | Nullable |
| `original_break_json` | `jsonb` | Nullable; original break intervals |
| `requested_break_json` | `jsonb` | Nullable; requested break intervals |
| `reason` | `varchar(255)` | Employee-provided reason |
| `notes` | `text` | Nullable |
| `status` | `varchar(20)` | `pending`, `approved`, `rejected`, `cancelled` |
| `requested_by_id` | `uuid` | FK -> users |
| `reviewed_by_id` | `uuid` | Nullable FK -> users |
| `reviewed_at` | `timestamptz` | Nullable |
| `review_comment` | `text` | Nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `break_records`

Individual break intervals per employee, taken manually or auto-detected by the agent's idle threshold.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `break_start` | `timestamptz` |  |
| `break_end` | `timestamptz` | Null if ongoing |
| `break_type` | `varchar(30)` | `lunch`, `prayer`, `smoke`, `personal`, `other` |
| `auto_detected` | `boolean` | True if detected by agent idle threshold |
| `created_at` | `timestamptz` |  |

### `device_sessions`

Active/idle periods per registered device, used as evidence when computing presence and activity rates.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `device_id` | `uuid` | FK -> registered_agents |
| `session_start` | `timestamptz` | When active period began |
| `session_end` | `timestamptz` | When active period ended (null if ongoing) |
| `active_minutes` | `int` | Minutes with input activity |
| `idle_minutes` | `int` | Minutes without input |
| `active_percentage` | `decimal(5,2)` | `active / (active + idle) * 100` |

### `work_schedules`

Work schedule header; per-day pattern lives in `work_schedule_days`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; selected topbar Company context |
| `name` | `varchar(100)` | Schedule title |
| `country_code` | `char(2)` | Nullable; holiday source only. Does not define schedule timezone |
| `pull_public_holidays` | `boolean` | True to pull public holidays from country_code |
| `timezone` | `varchar(50)` | IANA timezone. Defaults to `legal_entities.timezone`; all `work_schedule_days` times interpreted in this timezone |
| `default_for_new_employee` | `boolean` | Default assignment behavior for new employees in the selected company context |
| `is_active` | `boolean` |  |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `work_schedule_days`

Per-day work pattern; exactly 7 rows per schedule (day_of_week 1-7).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `work_schedule_id` | `uuid` | FK -> work_schedules |
| `day_of_week` | `smallint` | 1=Monday ... 7=Sunday |
| `is_working_day` | `boolean` | False for off days |
| `work_time_type` | `varchar(20)` | `fixed` or `flexible`; null when is_working_day = false |
| `start_time` | `time` | Nullable; required when work_time_type = fixed |
| `end_time` | `time` | Nullable; required when work_time_type = fixed |
| `required_work_minutes` | `int` | Nullable; required when work_time_type = flexible |
| `break_type` | `varchar(20)` | `none`, `fixed`, or `flexible`; null when is_working_day = false |
| `break_start_time` | `time` | Nullable; required when break_type = fixed |
| `break_end_time` | `time` | Nullable; required when break_type = fixed |
| `break_duration_minutes` | `int` | Nullable; required when break_type = flexible |
| `expected_work_area` | `varchar(10)` | Nullable; `onsite`, `remote`, `either`, `field` |
| `is_overnight` | `boolean` | True if end_time < start_time (shift crosses midnight) |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `work_schedule_holidays`

Per-schedule holiday selections (from country `public_holidays` or manually added).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `work_schedule_id` | `uuid` | FK -> work_schedules |
| `public_holiday_id` | `uuid` | Nullable FK -> public_holidays. Populated when `source` = `country_public_holiday` |
| `date` | `date` | Holiday date |
| `name` | `varchar(100)` | Holiday name |
| `source` | `varchar(30)` | `country_public_holiday` or `manual` |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |

### `schedule_assignments`

Date-effective assignment of a work schedule to company, department, position, or employee override.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; selected topbar Company context |
| `work_schedule_id` | `uuid` | FK -> work_schedules |
| `assignment_type` | `varchar(30)` | `full_company`, `department`, `position`, `employee` |
| `department_id` | `uuid` | Nullable FK -> departments; required for department assignment |
| `position_id` | `uuid` | Nullable FK -> positions; required for position assignment |
| `employee_id` | `uuid` | Nullable FK -> employees; required for employee override |
| `effective_from` | `date` |  |
| `effective_to` | `date` | Nullable - null means currently active |
| `is_default_for_new_employee` | `boolean` | Applies only where marked as the selected company default |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |

### `public_holidays`

Country-level (or tenant-overridden) holiday dates that feed per-schedule holiday selection and the Calendar.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants (nullable - null means country-default) |
| `country_id` | `uuid` | FK -> countries |
| `date` | `date` |  |
| `name` | `varchar(100)` | e.g., "National Day" |
| `is_mandatory` | `boolean` | False allows tenant-level override |

### `shifts`

Named shift definitions - reusable building blocks of schedules.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(100)` | e.g., "Morning Shift", "Night Shift" |
| `start_time` | `time` | e.g., `09:00` |
| `end_time` | `time` | e.g., `18:00` |
| `break_minutes` | `int` | Expected total break duration |
| `is_overnight` | `boolean` | True if end_time < start_time |
| `is_active` | `boolean` |  |
| `created_at` | `timestamptz` |  |

### `shift_assignments`

Maps an employee to a specific shift for a specific date.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `shift_id` | `uuid` | FK -> shifts |
| `date` | `date` |  |
| `expected_work_area` | `varchar(10)` | Nullable override: `onsite`, `remote`, `either`, `field` |
| `is_override` | `boolean` | True if manually overriding the employee's default schedule |
| `created_at` | `timestamptz` |  |

### `roster_periods`

A planning window for shift rosters (e.g., a week or fortnight).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(100)` | e.g., "Week 15 - Apr 7-13" |
| `start_date` | `date` |  |
| `end_date` | `date` |  |
| `status` | `varchar(20)` | `draft`, `published`, `locked` |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |

### `roster_entries`

An employee's placement within a roster period.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `roster_period_id` | `uuid` | FK -> roster_periods |
| `employee_id` | `uuid` | FK -> employees |
| `shift_id` | `uuid` | FK -> shifts |
| `date` | `date` |  |
| `expected_work_area` | `varchar(10)` | Nullable override: `onsite`, `remote`, `either`, `field` |

### `work_area_change_requests`

One-day expected work area overrides (distinct from `remote_work_location_change_requests`).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `legal_entity_id` | `uuid` | FK -> legal_entities; selected topbar Company context |
| `date` | `date` | The date the override applies to |
| `shift_assignment_id` | `uuid` | Nullable FK -> shift_assignments |
| `current_expected_work_area` | `varchar(10)` | The resolved expected work area before the change request |
| `requested_work_area` | `varchar(10)` | `onsite`, `remote`, `either`, `field` |
| `reason` | `text` | Employee-provided reason |
| `status` | `varchar(20)` | `pending`, `approved`, `rejected`, `cancelled` |
| `requested_at` | `timestamptz` |  |
| `reviewed_by_id` | `uuid` | Nullable FK -> users |
| `reviewed_at` | `timestamptz` | Nullable |
| `review_comment` | `text` | Nullable |

### `clock_in_policies`

Clock-in source control, verification behavior, scope, and effective dates per company context.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; selected topbar Company context |
| `name` | `varchar(120)` | Policy name |
| `scope_type` | `varchar(30)` | `full_company`, `department`, `position`, `employee` |
| `department_ids` | `uuid[]` | Nullable; required when scope_type = department |
| `position_ids` | `uuid[]` | Nullable; required when scope_type = position |
| `employee_ids` | `uuid[]` | Nullable; required when scope_type = employee |
| `effective_from` | `date` | |
| `effective_to` | `date` | Nullable - null means currently active |
| `location_verification_required` | `boolean` | |
| `allowed_radius_meters` | `int` | Nullable; applies to both onsite office and approved remote work location |
| `onsite_biometric_enabled` | `boolean` | |
| `onsite_web_enabled` | `boolean` | |
| `onsite_tray_enabled` | `boolean` | |
| `onsite_photo_required` | `boolean` | |
| `remote_biometric_enabled` | `boolean` | |
| `remote_web_enabled` | `boolean` | |
| `remote_tray_enabled` | `boolean` | |
| `remote_photo_required` | `boolean` | |
| `either_biometric_enabled` | `boolean` | Applies when expected_work_area = either (renamed from hybrid_biometric_enabled) |
| `either_web_enabled` | `boolean` | Web/app clock-in allowed when expected_work_area = either |
| `either_tray_enabled` | `boolean` | Desktop tray clock-in allowed when expected_work_area = either |
| `either_photo_required` | `boolean` | Photo required when expected_work_area = either |
| `either_location_check_required` | `boolean` | Location check on either days; overrides general location_verification_required for this work area |
| `either_source_rule` | `varchar(30)` | How work area resolves on either days: `onsite`, `remote`, `employee_choice` |
| `field_biometric_enabled` | `boolean` | |
| `field_web_enabled` | `boolean` | Web/app clock-in allowed when expected_work_area = field |
| `field_tray_enabled` | `boolean` | |
| `field_photo_requirement` | `varchar(20)` | `off`, `optional`, `required` |
| `correction_requires_approval` | `boolean` | Whether attendance corrections require manager approval |
| `notification_recipient_resolver` | `varchar(50)` | e.g., `management_coverage_owner` |
| `is_active` | `boolean` | |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `biometric_outage_fallbacks`

Temporary website/tray attendance fallback windows for verified biometric-device outages.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; selected Company context |
| `scope_type` | `varchar(30)` | `full_company`, `department`, `employee` |
| `department_ids` | `uuid[]` | Required for department scope |
| `employee_ids` | `uuid[]` | Required for employee scope |
| `affected_device_id` | `uuid` | Nullable; registered device with verified outage |
| `starts_at` | `timestamptz` | Fallback start date/time |
| `ends_at` | `timestamptz` | Fallback end date/time |
| `status` | `varchar(20)` | `scheduled`, `active`, `resolved`, `expired` |
| `reason` | `text` | Verified outage reason |
| `created_by_id` | `uuid` | FK -> users |
| `resolved_by_id` | `uuid` | Nullable FK -> users |
| `resolved_at` | `timestamptz` | Nullable; early resolution time |

**Foreign Keys:** `tenant_id` -> tenants, `legal_entity_id` -> legal_entities, `affected_device_id` -> biometric_devices, `created_by_id`, `resolved_by_id` -> users

**Validation:** `ends_at` must be later than `starts_at`; scope targets must belong to the selected Company; `status = resolved` requires `resolved_by_id` and `resolved_at`; and scheduled/active windows must not overlap for the same effective Company/scope targets and affected device.

### `clock_in_late_deduction_rules`

Progressive late-arrival deduction rule brackets for a Clock-in Policy.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `clock_in_policy_id` | `uuid` | FK -> clock_in_policies |
| `late_arrival_minute` | `int` | Bracket threshold in minutes; must be positive |
| `multiplier` | `decimal(5,2)` | Multiplier applied to late minutes in this bracket; 0 = no deduction (free range) |
| `time_off_type_id` | `uuid` | FK -> time_off_types; the Time Off type to deduct from |
| `is_active` | `boolean` | |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

---

## Identity Verification (8 tables)

### `verification_policies`

Tenant policy for when and how photo identity verification is required (clock-in/out photos, on-demand capture, match threshold, enrollment mode).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants, UNIQUE |
| `require_photo_clock_in` | `boolean` | Require camera photo at app/tray clock-in |
| `require_photo_clock_out` | `boolean` | Require camera photo at app/tray clock-out |
| `camera_photo_verification_enabled` | `boolean` | Allows authorized on-demand camera photo capture |
| `absence_photo_capture_enabled` | `boolean` | Camera photo capture on absence deviation |
| `photo_capture_context_scope` | `varchar(20)` | `remote_only`, `onsite_only`, `remote_and_onsite`, `disabled` |
| `match_threshold` | `decimal(5,2)` | Minimum confidence score to pass (default 80.0) |
| `reference_enrollment_mode` | `varchar(30)` | `manual_review` or `trusted_sso_auto_approve` |
| `block_monitoring_until_reference_approved` | `boolean` | If true, agent collection waits for approved reference |
| `is_active` | `boolean` | Master toggle |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `verification_records`

One row per identity verification attempt/challenge (photo or biometric) with its confidence, result, challenge timing, and reviewer decision.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `verified_at` | `timestamptz` | When verification occurred |
| `method` | `varchar(20)` | `photo`, `biometric`, `on_demand_photo` |
| `match_confidence` | `decimal(5,2)` | 0-100 confidence score |
| `status` | `varchar(20)` | `pending_review`, `verified`, `failed`, `skipped`, `expired` |
| `agent_id` | `uuid` | Nullable FK -> registered_agents; set for WorkPulse/desktop-agent photo verification |
| `biometric_device_id` | `uuid` | Nullable FK -> biometric_devices; set for physical terminal/device verification |
| `failure_reason` | `varchar(255)` | Nullable - why verification failed |
| `trigger` | `varchar(20)` | `on_demand`, `clock_in`, `clock_out`, `absence_detected`, `biometric_scan` |
| `requested_by_id` | `uuid` | Nullable FK -> users (who requested, for on-demand captures) |
| `alert_id` | `uuid` | Nullable - linked alert/notification ID |
| `requested_at` | `timestamptz` | Nullable - when backend creates the photo challenge |
| `delivered_at` | `timestamptz` | Nullable - when agent receives the command |
| `submitted_at` | `timestamptz` | Nullable - when employee submits/captures the photo |
| `expires_at` | `timestamptz` | Nullable - challenge expiry time |
| `response_duration_seconds` | `int` | Nullable - `submitted_at - requested_at` |
| `reviewed_by_id` | `uuid` | Nullable FK -> users - reviewer who assessed the result |
| `reviewed_at` | `timestamptz` | Nullable - when reviewer assessed the result |
| `review_status` | `varchar(20)` | Nullable - `pending`, `confirmed_mismatch`, `dismissed_false_positive` |
| `created_at` | `timestamptz` |  |

### `verification_evidence_assets`

Camera/photo evidence for identity verification, presence, or attendance workflows (never in `entity_assets`).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `verification_record_id` | `uuid` | Nullable FK -> verification_records |
| `presence_session_id` | `uuid` | Nullable FK -> presence_sessions |
| `attendance_event_id` | `uuid` | Nullable attendance event/source ID |
| `biometric_event_id` | `uuid` | Nullable FK -> biometric_events |
| `file_record_id` | `uuid` | FK -> file_records |
| `evidence_type` | `varchar(40)` | `identity_verification_photo`, `clock_in_photo`, `clock_out_photo`, `verification_failure_photo` |
| `trigger_type` | `varchar(20)` | `on_demand`, `clock_in`, `clock_out`, `absence_detected` |
| `captured_at` | `timestamptz` | When the evidence was captured |
| `agent_id` | `uuid` | Nullable FK -> registered_agents when evidence came from WorkPulse/desktop agent |
| `biometric_device_id` | `uuid` | Nullable FK -> biometric_devices when evidence came from a physical terminal/gateway |
| `retention_policy_id` | `uuid` | Nullable FK -> retention_policies |
| `legal_hold_id` | `uuid` | Nullable FK -> legal_holds |
| `metadata` | `jsonb` | Safe non-secret metadata |
| `created_at` | `timestamptz` |  |

### `verification_reference_photos`

Trusted employee reference images used for future photo comparisons.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `photo_file_id` | `uuid` | FK -> file_records |
| `source` | `varchar(30)` | `agent_first_sign_in`, `hr_verified_profile`, `admin_upload` |
| `status` | `varchar(20)` | `pending_review`, `approved`, `rejected`, `replaced`, `revoked` |
| `captured_device_id` | `uuid` | Nullable FK -> registered_agents |
| `captured_at` | `timestamptz` | When the reference candidate was captured |
| `reviewed_by_id` | `uuid` | Nullable FK -> users |
| `reviewed_at` | `timestamptz` | Nullable |
| `review_comment` | `varchar(255)` | Nullable |
| `legal_acceptance_record_id` | `uuid` | FK -> legal_acceptance_records for photo/biometric notice or consent |
| `is_active` | `boolean` | Only one approved active reference per employee |
| `created_at` | `timestamptz` |  |

**Unique:** `(tenant_id, employee_id) WHERE is_active = true`

### `biometric_devices`

Canonical table for physical attendance/biometric terminals (face, fingerprint, RFID/card, PIN, kiosk).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; required policy boundary for the device |
| `device_code` | `varchar(50)` | Unique device/terminal code within the tenant |
| `device_name` | `varchar(100)` | Human-readable name |
| `vendor` | `varchar(100)` | e.g. ZKTeco, Suprema, Hikvision, Anviz, ESSL, Matrix |
| `model` | `varchar(100)` | Device model |
| `connection_method` | `varchar(30)` | `direct_webhook`, `vendor_middleware`, `local_gateway`, `polling_api`, `manual_import` |
| `webhook_url` | `varchar(500)` | Nullable callback URL for push-style integrations |
| `vendor_middleware_url` | `varchar(500)` | Nullable local/vendor middleware or gateway URL |
| `external_device_ref` | `varchar(100)` | Vendor device identifier used by middleware, gateway, or import files |
| `api_key_encrypted` | `bytea` | HMAC/API key; encrypted at rest via `IEncryptionService` |
| `supported_auth_methods` | `jsonb` | Backend-normalized capabilities, e.g. `face`, `fingerprint`, `rfid_card`, `pin` |
| `enabled_auth_methods` | `jsonb` | Tenant-enabled punch methods for this device |
| `status` | `varchar(20)` | `active`, `offline`, `maintenance`, `disabled` |
| `last_heartbeat_at` | `timestamptz` |  |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `biometric_enrollments`

Which employees are enrolled on which terminal and modality, with the GDPR/PDPA consent flag (stores a template reference, never the raw biometric).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `biometric_device_id` | `uuid` | FK -> biometric_devices |
| `enrolled_at` | `timestamptz` |  |
| `consent_given` | `boolean` | GDPR/PDPA - must be true |
| `modality` | `varchar(30)` | `fingerprint`, `face`, `palm_vein`, `iris`, `other`; Phase 1 prioritizes fingerprint and face |
| `template_hash` | `varchar(128)` | Device-local biometric template reference (not the raw template itself) |
| `is_active` | `boolean` |  |

### `biometric_events`

Punch events (clock in/out, break start/end) coming from physical terminals, with the auth method used and the device verification result.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `biometric_device_id` | `uuid` | FK -> biometric_devices |
| `event_type` | `varchar(20)` | `clock_in`, `clock_out`, `break_start`, `break_end` |
| `auth_method` | `varchar(40)` | `fingerprint`, `face`, `rfid_card`, `pin`, `card_plus_face`, `card_plus_fingerprint`, `manual` |
| `modality` | `varchar(30)` | Nullable; `fingerprint`, `face`, `palm_vein`, `iris`, `other` when a biometric factor was used |
| `captured_at` | `timestamptz` |  |
| `verified` | `boolean` | Device verification result |
| `created_at` | `timestamptz` |  |

### `biometric_audit_logs`

Device-level events (heartbeats, tamper detection, firmware updates, errors) for terminal health monitoring and audit.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `biometric_device_id` | `uuid` | FK -> biometric_devices |
| `event_type` | `varchar(50)` | `heartbeat`, `tamper_detected`, `firmware_update`, `error` |
| `details_json` | `jsonb` | Event-specific details |
| `recorded_at` | `timestamptz` |  |

---

## Productivity Analytics (5 tables)

### `daily_employee_report`

Pre-computed per-employee daily productivity report combining presence, activity, and Work output into the final productivity score.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `date` | `date` |  |
| `total_hours` | `decimal(5,2)` | From presence sessions |
| `active_hours` | `decimal(5,2)` | From activity summaries |
| `idle_hours` | `decimal(5,2)` |  |
| `meeting_hours` | `decimal(5,2)` |  |
| `active_percentage` | `decimal(5,2)` | Activity rate, not final productivity |
| `productive_app_hours` | `decimal(5,2)` | Work-classified app/domain time |
| `focus_hours` | `decimal(5,2)` | Deep-focus time |
| `activity_score` | `decimal(5,2)` | Monitoring-derived score, 0-100 |
| `work_output_score` | `decimal(5,2)` | Nullable WorkSync output score |
| `productivity_score` | `decimal(5,2)` | Final score for reporting/reviews |
| `productivity_score_basis` | `varchar(30)` | `composite`, `activity_only`, `worksync_only`, `insufficient_data` |
| `data_coverage_percentage` | `decimal(5,2)` | Evidence completeness/confidence |
| `top_apps_json` | `jsonb` | Top 5 apps with time |
| `intensity_score` | `decimal(5,2)` | Average intensity for the day |
| `device_split_json` | `jsonb` | `{"laptop": 85, "mobile_estimate": 15}` |
| `exceptions_count` | `int` | Alerts triggered this day |
| `anomaly_flags_json` | `jsonb` | Flagged anomalies |
| `created_at` | `timestamptz` |  |

### `weekly_employee_report`

Weekly per-employee productivity rollup with week-over-week trend data.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `week_start` | `date` | Monday of the week |
| `total_hours` | `decimal(6,2)` |  |
| `active_hours` | `decimal(6,2)` |  |
| `idle_hours` | `decimal(6,2)` |  |
| `meeting_hours` | `decimal(6,2)` |  |
| `active_percentage` | `decimal(5,2)` | Activity rate |
| `productive_app_hours` | `decimal(6,2)` |  |
| `focus_hours` | `decimal(6,2)` |  |
| `activity_score_avg` | `decimal(5,2)` |  |
| `work_output_score_avg` | `decimal(5,2)` | Nullable |
| `productivity_score` | `decimal(5,2)` |  |
| `productivity_score_basis` | `varchar(30)` | `composite`, `activity_only`, `worksync_only`, `insufficient_data` |
| `data_coverage_percentage` | `decimal(5,2)` |  |
| `intensity_avg` | `decimal(5,2)` |  |
| `exceptions_count` | `int` |  |
| `trend_vs_previous_week_json` | `jsonb` | `{"active_pct_change": +5.2, "hours_change": -0.5}` |
| `created_at` | `timestamptz` |  |

### `monthly_employee_report`

Monthly per-employee productivity rollup with performance patterns and in-department ranking.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `year` | `int` |  |
| `month` | `int` | 1-12 |
| `total_hours` | `decimal(7,2)` |  |
| `active_hours` | `decimal(7,2)` |  |
| `idle_hours` | `decimal(7,2)` |  |
| `meeting_hours` | `decimal(7,2)` |  |
| `active_percentage` | `decimal(5,2)` | Activity rate |
| `productive_app_hours` | `decimal(7,2)` |  |
| `focus_hours` | `decimal(7,2)` |  |
| `activity_score_avg` | `decimal(5,2)` |  |
| `work_output_score_avg` | `decimal(5,2)` | Nullable |
| `productivity_score` | `decimal(5,2)` |  |
| `productivity_score_basis` | `varchar(30)` | `composite`, `activity_only`, `worksync_only`, `insufficient_data` |
| `data_coverage_percentage` | `decimal(5,2)` |  |
| `intensity_avg` | `decimal(5,2)` |  |
| `exceptions_count` | `int` |  |
| `performance_pattern_json` | `jsonb` | Weekday patterns, peak hours |
| `comparative_rank_in_department` | `int` | Rank by active% within department |
| `created_at` | `timestamptz` |  |

### `monitoring_snapshot`

Tenant-wide daily monitoring aggregate (averages, exception totals, department breakdown) powering org-level dashboards.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `date` | `date` |  |
| `total_employees` | `int` | Active employees count |
| `active_count` | `int` | Employees with activity this day |
| `avg_active_percentage` | `decimal(5,2)` | Tenant-wide activity-rate average |
| `avg_activity_score` | `decimal(5,2)` |  |
| `avg_work_output_score` | `decimal(5,2)` | Nullable |
| `avg_productivity_score` | `decimal(5,2)` |  |
| `avg_data_coverage_percentage` | `decimal(5,2)` |  |
| `avg_meeting_percentage` | `decimal(5,2)` |  |
| `total_exceptions` | `int` | Total alerts generated |
| `top_exception_types_json` | `jsonb` | Most common exception types |
| `department_breakdown_json` | `jsonb` | Per-department active% |
| `created_at` | `timestamptz` |  |

### `wms_productivity_snapshots`

Work Management-derived task productivity metrics per employee per period.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees |
| `period_type` | `varchar(10)` | `daily`, `weekly`, `monthly` |
| `period_start` | `date` | |
| `period_end` | `date` | |
| `tasks_completed` | `int` | |
| `tasks_on_time` | `int` | |
| `on_time_delivery_rate` | `decimal(5,2)` | 0-100 percentage |
| `work_output_score` | `decimal(5,2)` | Work Management-calculated output score (0-100) |
| `productivity_score` | `decimal(5,2)` | Deprecated alias for `work_output_score` during migration |
| `active_projects_count` | `int` | |
| `submitted_at` | `timestamptz` | When Work Management submitted this snapshot |
| `created_at` | `timestamptz` | |

**Unique:** `(tenant_id, employee_id, period_type, period_start)`

---

# Pillar 3 - Work Management

> Excluded as Phase 2 per schema files: `workspace_teams_links`, `teams_member_sync_status`, `project_workspaces` (reference only), all of wms-chat, and only `task_automation_rules` from wms-integrations. Customize Dashboard is Phase 2 but has no committed tables. The five sprint-planning tables and six GitHub repository-integration tables are Phase 1.

## Foundation + Projects + Objectives (15 tables)

> **Scope note (2026-08-03):** Workspaces are not part of Work Management Phase 1 — `workspaces`, `workspace_roles`, and `workspace_members` are removed below (previously documented here, never implemented in code). Project/Objective visibility, membership, and invitations are Objective-scoped, not workspace-scoped. `objective_participants` is also removed — Objective participation is represented only through `project_members`. `objective_categories`, `key_results`, and `okr_check_ins` remain documented below unchanged, but are **deferred**: not part of the current Objectives implementation (the `objectives` table below has no `category_id` for now). Project logo/cover attachment goes through the existing generic `entity_assets` table (see Infrastructure section above), not a `projects.logo_file_id` column.
>
> **Known follow-up (not resolved here):** removing `workspaces` leaves dangling `workspace_id` FK references documented on `key_results` (deferred, this section), and on `tasks`/`time_logs` (Task Management + Worklogs), `documents`/`wiki_pages` (Collaboration), and `repositories` (GitHub Repository Integration) further down this file. Those five tables belong to later, not-yet-scoped Work Management phases (Task Management, Sprint Planning, Collaboration, GitHub Integration) — resolving their workspace dependency is deferred to when each of those phases is actually planned, not fixed as part of this Projects/Objectives build.

### `project_categories`

Tenant-scoped user-defined project categories, selected during Project creation.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(100)` | Trimmed user-visible name |
| `is_active` | `boolean` | default true |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

**Unique:** normalized case-insensitive `(tenant_id, name)`

**Rule:** Project categories and Objective categories are independent catalogs. An unused category may be deleted. A referenced category requires a replacement category; reassignment and deletion occur in one transaction. Categories do not control access, lifecycle, or workflow.

### `projects`

Work containers holding Objectives and tasks. No workspace dependency in Phase 1 — visibility is membership-only through `project_members`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants; trusted server-resolved, never frontend-supplied |
| `owning_legal_entity_id` | `uuid` | FK -> legal_entities; set from active legal entity context, never frontend-supplied |
| `category_id` | `uuid` | FK -> project_categories; must belong to current tenant |
| `name` | `varchar(200)` | |
| `identifier` | `varchar(20)` | Tenant-unique uppercase task key prefix; backend trims/uppercases/validates; immutable after first task exists |
| `next_task_number` | `bigint` | default 1; atomically incremented when creating a task |
| `description` | `text` | nullable |
| `lead_id` | `uuid` | FK -> users; Project Owner/Lead; equals the creator at creation time, never frontend-supplied; lead-transfer API is out of scope |
| `start_date` | `date` | |
| `target_date` | `date` | must not be earlier than start_date |
| `color` | `varchar(20)` | nullable |
| `actual_hours` | `numeric(18,2)` | nullable; non-negative when present |
| `allocated_hours` | `numeric(18,2)` | default 0; hours reserved by child Objectives and tasks |
| `completed_hours` | `numeric(18,2)` | default 0; credited Task time rolled up from Project Tasks |
| `is_active` | `boolean` | default true |
| `is_achieved` | `boolean` | default false; added 2026-08-08 (Achieve workflow). Lead-only, always-immediate completion state, independent of `is_active`. Requires every top-level milestone (direct child of the Default Objective) to already be Achieved |
| `achieved_at` | `timestamptz` | nullable; set when `is_achieved` flips true, cleared on Unachieve |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

Optimistic concurrency via PostgreSQL `xmin` (no explicit column). Project hour indicators are warning-only: planning over-allocation is `actual_hours IS NOT NULL AND allocated_hours > actual_hours`; execution overrun is `allocated_hours > 0 AND completed_hours > allocated_hours`. Over-allocation never blocks creation or edits.

**Forbidden:** `workspace_id`, `logo_file_id` (logo/cover goes through `entity_assets`, `owner_type = 'project'`), permanent public R2 URL, frontend-controlled `tenant_id`/`owning_legal_entity_id`/`lead_id` at creation, `is_private` (visibility is membership-only, not a flag), `icon_url` (superseded by the `entity_assets`-linked logo), free-form `status` string (superseded by `is_active`).

**Unique:** `(tenant_id, identifier)` normalized. **Indexes:** `(tenant_id, owning_legal_entity_id, updated_at)`, `(tenant_id, category_id, is_active)`.

### `project_members`

Source of truth for Project visibility and access. Every membership is Objective-specific — there is no project-wide-only membership row.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `project_id` | `uuid` | FK -> projects |
| `objective_id` | `uuid` | FK -> objectives; required, not null; must belong to `project_id` |
| `user_id` | `uuid` | FK -> users |
| `employee_id` | `uuid` | FK -> employees; must represent the same active tenant employee as `user_id` |
| `membership_source` | `varchar(30)` | `system` (creator, at Project creation) or `objective_invitation` (via accepted invitation) |
| `is_active` | `boolean` | false when removed, deactivated, or employee offboarded |
| `joined_at` | `timestamptz` | |
| `removed_at` | `timestamptz` | nullable; set on deactivation, cleared on reactivation |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

**Forbidden:** `role` (no role column — permission/business-scope checks are separate from membership), direct employee addition to a new Objective outside the invitation flow.

**Unique:** `(tenant_id, project_id, objective_id, user_id)`. **Indexes:** `(tenant_id, user_id, is_active, project_id)`, `(tenant_id, project_id, objective_id, is_active)`.

### `project_member_invitations`

Invitations that target one specific Objective; acceptance creates or reactivates the Objective-specific `project_members` row.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `project_id` | `uuid` | FK -> projects |
| `objective_id` | `uuid` | FK -> objectives; required; must belong to `project_id` |
| `invited_user_id` | `uuid` | FK -> users; resolved server-side from the selected Employee's linked user |
| `invited_employee_id` | `uuid` | FK -> employees; must be active and in current tenant |
| `status` | `varchar(20)` | `pending` / `accepted` / `declined` / `expired` / `cancelled` |
| `invited_by_id` | `uuid` | FK -> users |
| `decided_at` | `timestamptz` | nullable |
| `expires_at` | `timestamptz` | nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

**Forbidden:** `role` (no role concept on invitations either).

**Indexes:** `(tenant_id, invited_user_id, status)`; partial unique — at most one `pending` invitation per `(tenant_id, project_id, objective_id, invited_user_id)`.

### `project_link_invitations`

Admin-to-admin invitations to link two projects, carrying a proposed hour allocation; acceptance creates a `project_links` row.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `source_project_id` | `uuid` | FK -> projects; the parent project proposing the allocation |
| `target_project_id` | `uuid` | FK -> projects; the child project being invited |
| `tenant_id` | `uuid` | FK -> tenants |
| `allocated_hours` | `numeric(18,2)` | proposed hour allocation, shown to the invited admin before they accept |
| `invited_project_admin_id` | `uuid` | FK -> users |
| `status` | `varchar(20)` | pending / accepted / declined / expired / cancelled |
| `invited_by_id` | `uuid` | FK -> users |
| `decided_at` | `timestamptz` | nullable |
| `expires_at` | `timestamptz` | nullable |

### `project_links`

Created either after a `project_link_invitations` row is accepted (`created_via = 'invitation'`), or immediately when the initiator is admin on both projects (`created_via = 'direct_same_admin'`). Allocations contribute to reporting totals, but exceeding expected hours produces a warning and never blocks work.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `source_project_id` | `uuid` | FK -> projects; the parent project allocating hours |
| `target_project_id` | `uuid` | FK -> projects; the child project receiving the hour allocation |
| `tenant_id` | `uuid` | FK -> tenants |
| `allocated_hours` | `numeric(18,2)` | hours assigned from the source project to the linked project; over-allocation is reported but does not block work |
| `created_via` | `varchar(20)` | `invitation` / `direct_same_admin` |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `is_active` | `boolean` | |

**Unique:** `(target_project_id) WHERE is_active = true` — a project can have at most one active parent link at a time.

### `version_statuses`

Global (not tenant-scoped) fixed lookup, seeded with exactly three rows at startup — same shape and seeding mechanism (`LookupDataSeeder`, `Id`/`Code`/`Label`) as `employment_types`/`severities`/etc. Version status moves only through the signed-movement API (`PATCH .../versions/{id}/status`), never a free-form string.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `integer` | PK; fixed seed IDs, matches the repo's existing lookup-table convention (not `smallint` — no other lookup in this repo uses `smallint`) |
| `code` | `varchar(20)` | unique; `planned` / `released` / `archived` |
| `label` | `varchar(50)` | display name, e.g. `Planned` |

**Seed rows:** `1 = planned/Planned`, `2 = released/Released`, `3 = archived/Archived`.

### `versions`

Release versions of a project (planned/released/archived via `status_id`).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `project_id` | `uuid` | FK -> projects |
| `name` | `varchar(100)` | |
| `description` | `text` | nullable |
| `status_id` | `integer` | FK -> version_statuses; the Default Version created with a Project uses `1` (planned) |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

Optimistic concurrency via PostgreSQL `xmin`. **Forbidden:** `release_date` (moved to `release_calendar.scheduled_date`), free-form `status` string (superseded by `status_id`).

**Indexes:** `(tenant_id, project_id, status_id)`.

### `release_calendar`

Scheduled release reminder for a Version, owned by the Project creator only (not Project or Objective members in general).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `project_id` | `uuid` | FK -> projects |
| `version_id` | `uuid` | FK -> versions; must belong to `project_id` |
| `recipient_user_id` | `uuid` | FK -> users; the Project creator |
| `scheduled_date` | `date` | taken from the Project creation `releaseDate` field |
| `reminder_type` | `varchar(30)` | default `project_release` |
| `notes` | `text` | nullable |
| `is_active` | `boolean` | default true |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

**Forbidden:** `workspace_id`.

**Constraint:** at most one active `project_release` reminder per `(version_id, recipient_user_id)`. **Indexes:** `(tenant_id, recipient_user_id, scheduled_date, is_active)`.

### `labels`

Project-scoped labels, optionally set during Project creation.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `project_id` | `uuid` | FK -> projects |
| `name` | `varchar(50)` | trimmed; duplicates within the same creation request rejected before persistence |
| `color` | `varchar(20)` | |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

**Unique:** normalized case-insensitive `(tenant_id, project_id, name)`

---

### `objective_categories`

Tenant-scoped user-defined Objective categories, independent from project categories.

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(100)` | Trimmed user-visible name |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Unique:** `(tenant_id, lower(name))`

### `objectives`

Objectives and child Objectives (frontend may label these "Milestones"; the database/backend name stays `objectives`). Exactly one Default Objective per Project is the root; child Objectives nest under any Objective in the same Project.

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `project_id` | `uuid` | FK -> projects; the Default Objective's `project_id` equals its owning Project's ID; child Objectives use the same `project_id` |
| `parent_objective_id` | `uuid` | FK -> objectives, nullable; self-reference to another Objective, never to a Project ID; null only for the Default Objective |
| `is_default` | `boolean` | default false; at most one `true` row per `project_id` (partial unique index) |
| `title` | `varchar(255)` | for the Default Objective, mirrors `projects.name` and stays in sync on Project edit |
| `description` | `text` | nullable; for the Default Objective, mirrors `projects.description` |
| `owner_id` | `uuid` | FK -> users; the Objective's current Head. For a non-default Objective this is reassigned by Transfer; for the Default Objective it equals the Project's `lead_id` and is never transferred |
| `reporting_manager_id` | `uuid` | nullable; FK -> users; null only for the Default Objective. Documented as "frozen at creation" by the 2026-08-04 milestone-hierarchy plan, but the 2026-08-06 membership/Achieve plan made it dynamic — it tracks the *parent* Objective's *current* Head, cascaded to direct children only (one level) whenever a Transfer applies. Pending Delete/Edit/Transfer/Achieve/Unachieve requests on this Objective route to whoever holds this column at request-creation time |
| `is_active` | `boolean` | default true; the Default Objective cannot be deactivated while the Project remains active |
| `start_date` | `date` | for the Default Objective, mirrors `projects.start_date` |
| `end_date` | `date` | must not be earlier than start_date; for the Default Objective, mirrors `projects.target_date` |
| `progress` | `numeric(5,2)` | default 0 |
| `actual_hours` | `numeric(18,2)` | nullable; non-negative; for the Default Objective, mirrors `projects.actual_hours` |
| `allocated_hours` | `numeric(18,2)` | default 0; non-negative; Objective-specific, not mirrored from Project |
| `completed_hours` | `numeric(18,2)` | default 0; non-negative; Objective-specific, not mirrored from Project |
| `is_achieved` | `boolean` | default false; added 2026-08-08 (Achieve workflow). Requires every *direct* child Objective to already be Achieved (shallow check; transitively enforced bottom-up). Default Objective can never be Achieved directly — use the Project-level Achieve endpoint instead. Frozen for Edit/Transfer/member-add-remove while true (mirrors the existing `!is_active` freeze), but Delete is unaffected |
| `achieved_at` | `timestamptz` | nullable; set when `is_achieved` flips true, cleared on Unachieve |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

Optimistic concurrency via PostgreSQL `xmin`. Hour indicators are the same warning-only formulas as `projects` and never block creation or edits. **Deferred (not part of the current implementation):** `category_id` / `objective_categories` linkage, `quarter`. **Forbidden:** `workspace_id`.

**Indexes:** `(tenant_id, project_id, parent_objective_id)`, `(tenant_id, owner_id, is_active)`, `(tenant_id, project_id, is_achieved)`, partial unique one `is_default = true` row per `project_id`.

**Removed (2026-08-03):** `objective_participants` — Objective participation is represented only through `project_members` (every membership row is Objective-specific); this table previously described a separate, now-unused participation model.

### `objective_change_requests`

Added 2026-08-04/05 (milestone-hierarchy plan), extended 2026-08-08 (membership/Achieve plan) — undocumented here until the 2026-08-08 pass. One pending/decided Delete, conflicting-Edit, Transfer, Achieve, or Unachieve request on an Objective a non-creator Head cannot apply unilaterally. The Objective's own creator never needs approval for their own creation (design rule, unchanged since 2026-08-04) — these rows exist only for the non-creator path. Project-level actions (Edit/Delete/Achieve/Unachieve on `projects` itself) never create a row here — the Project is the tree's root with no Reporting Manager to route to.

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `objective_id` | `uuid` | FK -> objectives; `ON DELETE RESTRICT` |
| `request_type` | `varchar(20)` | `delete` / `edit` / `transfer` / `achieve` / `unachieve` (the last two added 2026-08-08) |
| `requested_by_id` | `uuid` | FK -> users |
| `reporting_manager_id` | `uuid` | FK -> users; snapshotted from `objectives.reporting_manager_id` at request-creation time — approving/rejecting never re-reads a possibly-since-changed live value |
| `status` | `varchar(20)` | `pending` / `approved` / `rejected` |
| `payload_json` | `jsonb` | nullable; proposed new field values for `edit`/`transfer`; null for `delete`/`achieve`/`unachieve` (state transitions carry no proposed values) |
| `decided_at` | `timestamptz` | nullable |
| `decided_by_id` | `uuid` | nullable; FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

**Indexes:** `(tenant_id, objective_id, status)`, `(tenant_id, reporting_manager_id, status)`. **Unique:** partial unique on `(tenant_id, objective_id) WHERE status = 'pending'` — at most one pending request per Objective, enforced at the DB level, not just in the handler.

### `key_results`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `objective_id` | `uuid` | FK -> objectives |
| `workspace_id` | `uuid` | FK -> workspaces |
| `title` | `varchar(255)` | |
| `owner_id` | `uuid` | FK -> users |
| `result_type` | `varchar(20)` | percentage / numeric / currency / boolean |
| `start_value` | `numeric(18,4)` | |
| `target_value` | `numeric(18,4)` | |
| `current_value` | `numeric(18,4)` | |
| `unit` | `varchar(20)` | nullable |
| `status` | `varchar(20)` | mirrors objective status |
| `progress` | `numeric(5,2)` | computed and clamped to 0-100 |

### `okr_check_ins`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `key_result_id` | `uuid` | FK -> key_results |
| `previous_value` | `numeric(18,4)` | |
| `new_value` | `numeric(18,4)` | |
| `note` | `text` | nullable |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |

---

## Task Management + Worklogs (15 tables)

### `task_statuses`

Configurable task-status definitions. Project rows are templates; each Objective receives an independent copy that its Objective Lead can customize.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `project_id` | `uuid` | FK -> projects |
| `objective_id` | `uuid` | FK -> objectives, nullable; null is a project template, set is an independent Objective status |
| `name` | `varchar(100)` | User-visible name unique within the template/Objective scope |
| `display_order` | `int` | Kanban-column order within the template/Objective scope |
| `requires_approval` | `boolean` | default false |
| `approver_id` | `uuid` | FK -> employees, nullable; required for approval-enabled Objective statuses and null on project templates |
| `marks_task_complete` | `boolean` | default false; explicitly controls task completion behavior |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `tasks`

Core work item table - tasks/bugs/stories/features with status, priority, dates, and subtask hierarchy; the hub the whole Work pillar revolves around.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `project_id` | `uuid` | FK -> projects |
| `workspace_id` | `uuid` | FK -> workspaces; copied from owning project workspace |
| `tenant_id` | `uuid` | FK -> tenants |
| `parent_task_id` | `uuid` | FK -> tasks, nullable; subtasks |
| `objective_id` | `uuid` | FK -> objectives; owning Objective and completed-hours rollup scope |
| `sprint_id` | `uuid` | Nullable FK -> sprints; denormalized current active membership |
| `version_id` | `uuid` | Nullable FK -> versions |
| `short_id` | `varchar(50)` | Tenant-unique immutable human-readable reference |
| `title` | `varchar(500)` | |
| `description` | `text` | nullable; rich text / markdown |
| `task_type` | `varchar(20)` | task / bug / story / feature |
| `status_id` | `uuid` | FK -> task_statuses; same Objective and Project as task |
| `priority` | `varchar(20)` | low / medium / high / critical |
| `story_points` | `int` | nullable |
| `due_date` | `date` | nullable |
| `estimated_hours` | `numeric(18,2)` | nullable; contributes to calculated Objective/project allocated hours without blocking overage |
| `completed_hours` | `numeric(18,2)` | default 0; credited work time from this Task's time logs |
| `progress_percent` | `int` | default 0; cumulative Task completion from 0 through 100 |
| `started_at` | `timestamptz` | nullable; set by first task Clock In or completed manual worklog |
| `completed_at` | `timestamptz` | nullable; set when completion behavior succeeds; a pending approval to reopen preserves it until approval clears it |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `time_logs`

Simple Phase 1 manual and timer-based Worklogs attached to tasks.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `workspace_id` | `uuid` | FK -> workspaces |
| `task_id` | `uuid` | FK -> tasks |
| `user_id` | `uuid` | FK -> users |
| `employee_id` | `uuid` | FK -> employees; resolved at write time |
| `logged_date` | `date` | Manual/same-date reporting date; cross-midnight credited daily attribution remains a product decision |
| `duration_minutes` | `int` | Raw whole minutes; manual value must be positive, while a stopped sub-minute timer may be 0 because exact timestamps remain authoritative |
| `credited_duration_minutes` | `int` | default 0; Task-attributed duration used for rollups |
| `description` | `text` | nullable |
| `source` | `varchar(20)` | Phase 1: `manual`, `timer` |
| `started_at` | `timestamptz` | nullable; timer sessions only |
| `ended_at` | `timestamptz` | nullable; manual rows have no time range, while timer rows remain null only until stopped |
| `progress_percent_after` | `int` | nullable; cumulative Task progress confirmed after timer stop |
| `progress_report_status` | `varchar(20)` | `not_required`, `pending`, `submitted` |
| `auto_stop_source` | `varchar(30)` | nullable; web/desktop/biometric/attendance auto-close source |
| `presence_session_id` | `uuid` | FK -> presence_sessions, nullable |
| `attribution_timezone` | `varchar(100)` | IANA timezone captured at timer start; nullable only for legacy/manual rows |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

A manual row is complete when its positive duration is stored and raw/credited minutes are equal. A timer row is complete when `ended_at IS NOT NULL`. Multiple different Task timers may be active, but only one active timer per `(user_id, task_id)` is allowed. Overlap is divided deterministically; only credited duration rolls up. Cross-midnight reporting uses the captured timezone and deterministic proportional largest-remainder allocation.

### `task_assignments`

Who is assigned to a task, enriched with HR availability check results (leave, schedule, inactive employee) at assignment time.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `task_id` | `uuid` | FK -> tasks |
| `user_id` | `uuid` | FK -> users |
| `employee_id` | `uuid` | FK -> employees; required for tenant employees |
| `assigned_by_id` | `uuid` | FK -> users |
| `assigned_at` | `timestamptz` | |
| `availability_status` | `varchar(20)` | `available`, `on_leave`, `outside_schedule`, `inactive_employee`, `unknown` |
| `availability_checked_at` | `timestamptz` | nullable |
| `availability_warning` | `text` | nullable |

**Unique:** `(task_id, user_id)`

### `task_checklists`

Named checklists inside a task for breaking work into checkable sub-items.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `task_id` | `uuid` | FK -> tasks |
| `title` | `varchar(255)` | |
| `position` | `int` | |
| `created_at` | `timestamptz` | |

### `task_comments`

Task-scoped comments with author editing and audit-preserving soft deletion.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `task_id` | `uuid` | FK -> tasks |
| `author_user_id` | `uuid` | FK -> users |
| `content` | `text` | Non-empty comment body |
| `is_edited` | `boolean` | default false |
| `edited_at` | `timestamptz` | nullable |
| `is_deleted` | `boolean` | default false |
| `deleted_at` | `timestamptz` | nullable |
| `created_at` | `timestamptz` | |

**Indexes:** `(tenant_id, task_id, created_at)`, `(author_user_id, created_at)`

### `task_checklist_items`

Individual checkable items within a task checklist, recording who checked them and when.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `checklist_id` | `uuid` | FK -> task_checklists |
| `text` | `varchar(500)` | |
| `is_checked` | `boolean` | default false |
| `position` | `int` | |
| `checked_by_id` | `uuid` | FK -> users, nullable |
| `checked_at` | `timestamptz` | nullable |

### `task_tags`

Join table applying project labels to tasks.

| Column | Type | Notes |
|:-------|:-----|:------|
| `task_id` | `uuid` | FK -> tasks |
| `label_id` | `uuid` | FK -> labels |

**PK:** `(task_id, label_id)`

### `task_approvals`

Approval requests created when a non-bypass actor drags a task into an approval-required Objective status. The task moves immediately and is rendered pending until decided.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `task_id` | `uuid` | FK -> tasks |
| `source_status_id` | `uuid` | FK -> task_statuses, nullable after a decided request's source status is later deleted |
| `target_status_id` | `uuid` | FK -> task_statuses, nullable after a decided request's target status is later deleted |
| `source_status_name` | `varchar(100)` | Immutable source-name snapshot |
| `target_status_name` | `varchar(100)` | Immutable target-name snapshot |
| `requested_by_id` | `uuid` | FK -> users |
| `approver_id` | `uuid` | FK -> employees; exactly one approver identity |
| `status` | `varchar(20)` | pending / approved / rejected / cancelled |
| `comment` | `text` | nullable |
| `requested_at` | `timestamptz` | |
| `decided_at` | `timestamptz` | nullable |

**Unique:** one pending approval per task. Approval leaves the task in the target status. If a pending move leaves a completion status, the original completion timestamp remains until approval clears it; rejection/cancellation restores the completion source without losing that timestamp. Rejection requires comment plus confirmed/corrected cumulative progress, restores the source, and appends progress history; a restored completion source requires 100 percent. Cancellation restores the source without progress editing. The configured approver or direct Objective Lead bypasses request creation when performing the drag themselves.

### `task_progress_updates`

Append-only cumulative Task-progress evidence.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `task_id` | `uuid` | FK -> tasks |
| `time_log_id` | `uuid` | FK -> time_logs, nullable |
| `task_approval_id` | `uuid` | FK -> task_approvals, nullable |
| `previous_percent` | `int` | 0-100 |
| `new_percent` | `int` | 0-100 |
| `source` | `varchar(40)` | Task Clock Out, attendance follow-up, Objective Lead adjustment, approval, or rejection |
| `changed_by_id` | `uuid` | FK -> users |
| `comment` | `text` | nullable; required for Objective Lead adjustment and rejection |
| `created_at` | `timestamptz` | |

Rows are immutable. Status rejection records a row even when the confirmed percentage is unchanged.

### `task_time_correction_requests`

Employee requests to correct credited minutes on completed Task timer logs while preserving raw timer evidence. Manual logs do not use this overlap-allocation correction flow.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `time_log_id` | `uuid` | FK -> time_logs |
| `task_id` | `uuid` | FK -> tasks |
| `requested_by_id` | `uuid` | FK -> users; time-log owner |
| `approver_id` | `uuid` | FK -> employees; current direct Objective Lead for pending routing |
| `original_credited_duration_minutes` | `int` | Immutable value at request time |
| `requested_credited_duration_minutes` | `int` | Non-negative requested value |
| `reason` | `text` | Required |
| `status` | `varchar(20)` | pending / approved / rejected / cancelled |
| `decision_comment` | `text` | nullable; required on rejection |
| `requested_at` | `timestamptz` | |
| `decided_at` | `timestamptz` | nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

Only the time-log owner requests/cancels. The direct Objective Lead is the single approver. Approval changes credited duration and rollups only; raw duration remains unchanged.

### `task_watchers`

Users following a task so they receive its notifications.

| Column | Type | Notes |
|:-------|:-----|:------|
| `task_id` | `uuid` | FK -> tasks |
| `user_id` | `uuid` | FK -> users |
| `employee_id` | `uuid` | FK -> employees; required for tenant employees |

**PK:** `(task_id, user_id)`

### `task_links`

Typed relationships between tasks (blocks / is blocked by / relates to / duplicates).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `source_task_id` | `uuid` | FK -> tasks |
| `target_task_id` | `uuid` | FK -> tasks |
| `link_type` | `varchar(30)` | blocks / is_blocked_by / relates_to / duplicates |

### `custom_fields`

Project-defined extra field definitions for tasks (text/number/date/select/user).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `project_id` | `uuid` | FK -> projects |
| `name` | `varchar(100)` | |
| `field_type` | `varchar(20)` | text / number / date / select / multiselect / user |
| `options_json` | `jsonb` | nullable; for select/multiselect options |
| `position` | `int` | |
| `is_required` | `boolean` | default false |

### `custom_field_values`

Per-task values for the project's custom fields, one typed value column per field type.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `task_id` | `uuid` | FK -> tasks |
| `field_id` | `uuid` | FK -> custom_fields |
| `value_text` | `text` | nullable |
| `value_number` | `numeric(18,4)` | nullable |
| `value_date` | `date` | nullable |
| `value_json` | `jsonb` | nullable; for multiselect / user arrays |

**Unique:** `(task_id, field_id)`

---

## Sprint Planning (5 tables)

### `sprints`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `project_id` | `uuid` | FK -> projects |
| `name` | `varchar(100)` | |
| `objective_id` | `uuid` | Nullable FK -> objectives; linked Objective/Sub-objective |
| `start_date` | `date` | |
| `end_date` | `date` | |
| `status` | `varchar(20)` | planning / active / completed |
| `completed_at` | `timestamptz` | nullable; set when completed |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `sprint_backlog_items`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `sprint_id` | `uuid` | FK -> sprints |
| `task_id` | `uuid` | FK -> tasks |
| `story_points` | `int` | nullable; locked at sprint start |
| `added_at` | `timestamptz` | |
| `added_by_id` | `uuid` | FK -> users |
| `removed_at` | `timestamptz` | nullable; null while task remains in sprint |

**Unique active membership:** `(sprint_id, task_id) WHERE removed_at IS NULL`

### `sprint_daily_snapshots`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `sprint_id` | `uuid` | FK -> sprints |
| `snapshot_date` | `date` | |
| `total_points` | `int` | All committed story points |
| `completed_points` | `int` | Story points of completed tasks |
| `remaining_points` | `int` | total_points - completed_points |
| `added_points` | `int` | Points added after sprint start |
| `removed_points` | `int` | Points removed after sprint start |

**Unique:** `(sprint_id, snapshot_date)`

### `sprint_reports`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `sprint_id` | `uuid` | FK -> sprints, UNIQUE |
| `velocity` | `numeric(8,2)` | Completed story points |
| `completed_points` | `int` | |
| `incomplete_points` | `int` | Points returned to backlog |
| `summary_json` | `jsonb` | Aggregate summary only |
| `created_at` | `timestamptz` | Created on sprint completion |

### `sprint_report_contributors`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `sprint_report_id` | `uuid` | FK -> sprint_reports |
| `user_id` | `uuid` | FK -> users |
| `employee_id` | `uuid` | FK -> employees |
| `completed_task_count` | `int` | |
| `completed_story_points` | `int` | |
| `review_count` | `int` | nullable |
| `rank` | `int` | Display order in report |

**Unique:** `(sprint_report_id, user_id)`

---

## Collaboration - Documents & Wiki (5 tables)

### `documents`

Work Management shared-file/document storage columns:

| Column | Type | Notes |
|:-------|:-----|:------|
| `workspace_id` | `uuid` | FK -> workspaces, nullable - Work Management scope |
| `project_id` | `uuid` | FK -> projects, nullable - project scope |
| `document_scope` | `varchar(30)` | company / legal_entity / employee / workspace / project |
| `locked_at` | `timestamptz` | nullable - set when document is approved and locked |
| `locked_by` | `uuid` | FK -> users, nullable - who approved and locked |
| `approved_version_id` | `uuid` | FK -> document_versions, nullable - the locked version |

**`status` enum:** draft / in_review / approved / published / archived (`approved` = locked; only admins can unlock)

### `document_versions`

Immutable version snapshots of a document's content, auto-numbered per document.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `document_id` | `uuid` | FK -> documents |
| `version_number` | `int` | Auto-incremented per document |
| `content_snapshot` | `text` | Full content at this version (or object storage key for large docs) |
| `change_summary` | `varchar(500)` | nullable - optional description of changes |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |

**Unique:** `(document_id, version_number)`

### `document_approvals`

Approval requests on documents; approving sets the document to `approved` and locks the exact submitted version.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `document_id` | `uuid` | FK -> documents |
| `document_version_id` | `uuid` | FK -> document_versions; immutable reviewed version |
| `requested_by_id` | `uuid` | FK -> users |
| `approver_id` | `uuid` | FK -> users |
| `status` | `varchar(20)` | pending / approved / rejected |
| `comment` | `text` | nullable |
| `requested_at` | `timestamptz` | request creation time |
| `decided_at` | `timestamptz` | nullable |

**Partial unique:** `(document_id) WHERE status = 'pending'` and `(document_id) WHERE status = 'approved'`.

### `wiki_pages`

Hierarchical project wiki content (markdown/rich text) with simple versioning and sibling ordering.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `workspace_id` | `uuid` | FK -> workspaces |
| `project_id` | `uuid` | FK -> projects, nullable; when set, must belong to workspace_id |
| `parent_page_id` | `uuid` | FK -> wiki_pages, nullable - hierarchical structure |
| `title` | `varchar(255)` | |
| `content` | `text` | Markdown / rich text |
| `author_id` | `uuid` | FK -> users |
| `last_edited_by` | `uuid` | FK -> users, nullable |
| `version_number` | `int` | Auto-incremented on each save |
| `is_published` | `boolean` | default true |
| `position` | `int` | Order among siblings |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `task_documents`

Durable link between a Work Management task and an editable document (not file attachments).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `task_id` | `uuid` | FK -> tasks |
| `document_id` | `uuid` | FK -> documents |
| `linked_by_id` | `uuid` | FK -> users |
| `linked_at` | `timestamptz` | |

**Unique:** `(task_id, document_id)`

---

## GitHub Repository Integration (6 tables)

These Phase 1 tables provide GitHub OAuth-backed repository connection, signed webhook ingestion, commit/pull-request/GitHub Actions synchronization, and task-to-repository links. Configurable trigger-condition-action automation remains Phase 2 and its `task_automation_rules` table is excluded.

### `repositories`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `workspace_id` | `uuid` | FK -> workspaces |
| `tenant_id` | `uuid` | FK -> tenants |
| `provider` | `varchar(20)` | Phase 1: `github` |
| `full_name` | `varchar(255)` | e.g. `org/repo` |
| `url` | `varchar(500)` | Clone URL |
| `default_branch` | `varchar(100)` | default `main` |
| `user_integration_connection_id` | `uuid` | FK -> user_integration_connections; personal GitHub OAuth connection used to connect/manage the repository |
| `external_webhook_id` | `varchar(100)` | Provider webhook ID returned during hook registration; used for reliable disconnect/reconciliation |
| `webhook_secret_encrypted` | `text` | Encrypted HMAC secret; never returned or logged after registration |
| `webhook_status` | `varchar(30)` | active / removal_pending / disconnected / error |
| `webhook_status_message` | `varchar(500)` | nullable sanitized operational summary; never credentials/provider payloads |
| `is_active` | `boolean` | default true |
| `created_at` | `timestamptz` | |

**Indexes:** `(workspace_id)`, `(tenant_id, provider)`
**Unique:** `(workspace_id, provider, full_name)`

### `task_repository_links`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `task_id` | `uuid` | FK -> tasks |
| `repository_id` | `uuid` | FK -> repositories |
| `linked_by_id` | `uuid` | FK -> users |
| `linked_at` | `timestamptz` | |

**Unique:** `(task_id, repository_id)`

### `code_activity_events`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `user_id` | `uuid` | FK -> users, nullable; resolved from GitHub identity |
| `tenant_id` | `uuid` | FK -> tenants |
| `repository_id` | `uuid` | FK -> repositories, nullable |
| `external_delivery_id` | `varchar(100)` | GitHub `X-GitHub-Delivery` value |
| `event_type` | `varchar(30)` | commit / push / pr_opened / pr_merged / pr_closed / branch_created / review_submitted / ci_started / ci_completed |
| `branch_name` | `varchar(255)` | nullable |
| `task_id` | `uuid` | FK -> tasks, nullable; detected from commit/PR reference |
| `event_metadata` | `jsonb` | Sanitized GitHub webhook payload |
| `occurred_at` | `timestamptz` | |
| `source` | `varchar(30)` | Phase 1: `github_webhook` |

**Indexes:** `(repository_id, occurred_at DESC)`, `(task_id)` where not null, `(tenant_id, occurred_at DESC)`
**Unique:** `(repository_id, external_delivery_id)`

### `commit_records`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `repository_id` | `uuid` | FK -> repositories |
| `sha` | `varchar(40)` | Git SHA |
| `author_user_id` | `uuid` | FK -> users, nullable |
| `message` | `text` | |
| `task_ids` | `uuid[]` | Best-effort task references extracted from message |
| `committed_at` | `timestamptz` | |
| `pushed_at` | `timestamptz` | nullable |

**Unique:** `(repository_id, sha)`

### `pull_request_records`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `repository_id` | `uuid` | FK -> repositories |
| `external_pr_id` | `varchar(50)` | GitHub pull-request ID |
| `title` | `varchar(500)` | |
| `url` | `varchar(500)` | |
| `status` | `varchar(20)` | open / merged / closed |
| `author_user_id` | `uuid` | FK -> users, nullable |
| `task_ids` | `uuid[]` | Task references extracted from title/body |
| `opened_at` | `timestamptz` | |
| `merged_at` | `timestamptz` | nullable |
| `closed_at` | `timestamptz` | nullable |

**Unique:** `(repository_id, external_pr_id)`

### `ci_pipeline_runs`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `repository_id` | `uuid` | FK -> repositories |
| `external_run_id` | `varchar(100)` | GitHub Actions workflow-run ID |
| `branch_name` | `varchar(255)` | |
| `status` | `varchar(20)` | pending / running / success / failed / cancelled |
| `task_ids` | `uuid[]` | Tasks linked to this branch |
| `started_at` | `timestamptz` | |
| `finished_at` | `timestamptz` | nullable |

**Indexes:** `(repository_id, branch_name)`, `(status)` where status in (pending, running)
**Unique:** `(repository_id, external_run_id)`

---

# Shared Foundation

## Reporting Engine (3 tables)

### `report_definitions`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(100)` | |
| `report_type` | `varchar(30)` | Supported report type |
| `template_id` | `uuid` | Nullable FK -> report_templates; null uses the system default for report_type |
| `parameters_json` | `jsonb` | Filters and date ranges |
| `schedule_cron` | `varchar(50)` | nullable; null for on-demand |
| `output_format` | `varchar(10)` | csv / xlsx |
| `recipients_json` | `jsonb` | Email recipients |
| `is_active` | `boolean` | |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |

### `report_executions`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `definition_id` | `uuid` | FK -> report_definitions |
| `status` | `varchar(20)` | queued / running / completed / failed |
| `file_record_id` | `uuid` | Nullable FK -> file_records; set after successful generation |
| `row_count` | `int` | nullable until completed |
| `started_at` | `timestamptz` | nullable until worker starts |
| `completed_at` | `timestamptz` | nullable until completed/failed |
| `error_message` | `text` | nullable |

### `report_templates`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(100)` | |
| `report_type` | `varchar(30)` | |
| `columns_json` | `jsonb` | Column definitions |
| `filters_json` | `jsonb` | Default filters |
| `is_system` | `boolean` | System templates cannot be deleted |

## Agent Gateway (6 tables)

### `registered_agents`

Every installed desktop agent device - identity, OS/agent version, heartbeat, and status; the anchor for all agent telemetry.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees (nullable - set at employee login) |
| `device_id` | `uuid` | Unique device identifier (generated at install) |
| `device_name` | `varchar(100)` | Computer hostname |
| `os_version` | `varchar(50)` | e.g., "Windows 11 23H2" |
| `agent_version` | `varchar(20)` | e.g., "1.0.0" |
| `registered_at` | `timestamptz` |  |
| `last_heartbeat_at` | `timestamptz` | Updated every 60s |
| `status` | `varchar(20)` | `active`, `inactive`, `revoked` |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `agent_sessions`

Tracks which employee is currently logged in on each device.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `device_id` | `uuid` | FK -> registered_agents.device_id |
| `tenant_id` | `uuid` | FK -> tenants |
| `employee_id` | `uuid` | FK -> employees - the currently logged-in employee |
| `is_active` | `boolean` | Only one active session per device at a time |
| `created_at` | `timestamptz` | When employee logged in via tray app |
| `ended_at` | `timestamptz` | Nullable - set on logout or next login |

**Unique partial index:** `(device_id) WHERE is_active = true`

### `agent_commands`

Server-to-agent command queue (screenshot/photo capture, monitoring start/stop, policy refresh) with delivery lifecycle and auto-expiry.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `agent_id` | `uuid` | FK -> registered_agents |
| `tenant_id` | `uuid` | FK -> tenants |
| `command_type` | `varchar(50)` | `capture_screenshot`, `capture_photo`, `capture_remote_work_location`, `start_monitoring`, `stop_monitoring`, `pause_monitoring`, `resume_monitoring`, `refresh_policy` |
| `requested_by` | `uuid` | FK -> users (authorized user who initiated) |
| `payload_json` | `jsonb` | Command-specific parameters |
| `status` | `varchar(20)` | `pending`, `delivered`, `completed`, `failed`, `expired` |
| `created_at` | `timestamptz` | When command was created |
| `delivered_at` | `timestamptz` | When agent acknowledged receipt |
| `completed_at` | `timestamptz` | When agent reported completion |
| `result_json` | `jsonb` | Result data (e.g., screenshot URL, error message) |
| `expires_at` | `timestamptz` | Auto-expire if not delivered (default: 5 min) |

### `agent_health_logs`

Agent self-reported resource usage, recent errors, and tamper detection - enforces the <50MB RAM / <2% CPU footprint and flags stopped/modified services.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `agent_id` | `uuid` | FK -> registered_agents |
| `tenant_id` | `uuid` | FK -> tenants |
| `reported_at` | `timestamptz` |  |
| `cpu_usage` | `decimal(5,2)` | Agent process CPU% |
| `memory_mb` | `int` | Agent process memory |
| `errors_json` | `jsonb` | Recent errors array |
| `tamper_detected` | `boolean` | Service stopped/modified |

### `agent_policies`

Per-device policy document the agent fetches and its collectors obey - the source of truth for what may be captured on each device.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `agent_id` | `uuid` | FK -> registered_agents |
| `tenant_id` | `uuid` | FK -> tenants |
| `policy_json` | `jsonb` | Policy toggles the collectors obey |
| `last_synced_at` | `timestamptz` | When agent last fetched this policy |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `agent_work_location_evidence`

Network and optional coarse location evidence captured only while monitoring is active.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `agent_id` | `uuid` | FK -> registered_agents |
| `employee_id` | `uuid` | FK -> employees |
| `presence_session_id` | `uuid` | FK -> presence_sessions, nullable until reconciliation |
| `captured_at` | `timestamptz` | Agent capture time |
| `received_at` | `timestamptz` | Server receive time |
| `public_ip` | `inet` | Captured from request metadata |
| `local_ip` | `inet` | Nullable |
| `wifi_ssid` | `varchar(255)` | Nullable, display only |
| `wifi_bssid_hash` | `varchar(100)` | Nullable, hashed access point identifier |
| `gateway_mac_hash` | `varchar(100)` | Nullable, hashed gateway identifier |
| `vpn_detected` | `boolean` | Default false |
| `coarse_location_json` | `jsonb` | Nullable; only when policy and OS permission allow |
| `match_status` | `varchar(20)` | `matched`, `mismatch`, `unknown`, `not_evaluated` |
| `confidence` | `varchar(20)` | `high`, `medium`, `low`, `unknown` |
| `matched_location_source` | `varchar(30)` | Nullable; `company_office`, `remote_profile`, or `none` |
| `matched_location_source_id` | `uuid` | Nullable; `legal_entities.id` or `employee_remote_work_profiles.id` |
| `created_at` | `timestamptz` |  |

---

## Shared Platform (54 tables)

> Excluded as Phase 2 per `shared-platform.md`: `approval_actions` and the Workflow/Automation Engine tables (`automation_definitions`, `automation_definition_versions`, `automation_templates`, `automation_runs`, `workflow_definitions`, `workflow_instances`, `workflow_step_instances`, `workflow_step_assignments`, `case_conversations`, `workflow_delivery_routes`, `workflow_steps`), plus the Microsoft Teams additions (`external_account_connections`, `microsoft_graph_tokens`, `teams_webhook_subscriptions`, `teams_delta_sync_state`). `refresh_tokens` is canonically defined and counted once under Auth & Security; Shared Platform does not define another variant.

### `idempotency_records`

Generic command/request idempotency records for retry-safe backend operations such as tenant provisioning, role template application, configuration template application, payment/webhook intake, and long-running admin/system operations.

Columns:
- `id` uuid PK
- `key` varchar(200) not null
- `scope` varchar(100) not null
- `requester_type` varchar(30) nullable, e.g. `tenant_user`, `platform_user`, `system`, `webhook`
- `requester_id` uuid nullable
- `tenant_id` uuid nullable FK -> tenants
- `request_hash` varchar(128) nullable
- `status` varchar(30) not null, e.g. `processing`, `completed`, `failed`, `expired`
- `response_status` integer nullable
- `response_body_json` jsonb nullable
- `locked_until` timestamptz nullable
- `expires_at` timestamptz not null
- `created_at` timestamptz not null
- `updated_at` timestamptz nullable

Unique:
- `(scope, key, requester_id)`

Rules:
- Use idempotency_records for generic command/request idempotency.
- Do not use idempotency_records to duplicate provider-specific idempotency where a canonical table already stores provider event identity.
- email_delivery_logs.provider_event_id handles email provider webhook idempotency.
- external_calendar_event_links handles calendar sync event identity.
- payment/webhook event tables use provider event IDs directly when those tables own the external event lifecycle.

### `api_keys`

Tenant-scoped API keys (stored hashed) with scopes and expiry for programmatic access to the API.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `name` | `varchar(100)` | Friendly name |
| `key_hash` | `varchar(255)` | SHA-256 hash (never store raw) |
| `key_prefix` | `varchar(10)` | First 8 chars for identification |
| `scopes` | `jsonb` | Permitted API scopes |
| `expires_at` | `timestamptz` | Nullable |
| `is_active` | `boolean` |  |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |
| `last_used_at` | `timestamptz` | Nullable |

### `compliance_exports`

GDPR-style data requests (subject access, portability, erasure) and their processing state and result file.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `requested_by_id` | `uuid` | FK -> users |
| `export_type` | `varchar(30)` | `subject_access`, `data_portability`, `erasure` |
| `scope` | `varchar(30)` | `full`, `partial` |
| `target_user_id` | `uuid` | FK -> users (whose data) |
| `status` | `varchar(20)` | `pending`, `processing`, `completed`, `failed` |
| `file_record_id` | `uuid` | Nullable FK -> file_records; private export object available only after scanning |
| `requested_at` | `timestamptz` |  |
| `completed_at` | `timestamptz` | Nullable |

### `escalation_rules`

Workflow SLA timeouts (distinct from Exception Engine `escalation_chains`).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `resource_type` | `varchar(50)` | e.g., `time_off_request`, `expense_claim` |
| `trigger_condition` | `varchar(100)` | e.g., `status = 'pending'` |
| `sla_hours` | `integer` | Hours before escalation fires |
| `action_type` | `varchar(30)` | `remind`, `escalate`, `auto_approve` |
| `escalate_to_role_id` | `uuid` | FK -> roles (nullable) |
| `notification_template_id` | `uuid` | FK -> notification_templates |
| `is_active` | `boolean` |  |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |

### `global_app_catalog`

Platform-wide application catalog (managed by operators) that seeds tenant allowlists and auto-classifies observed applications by `process_name`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `app_name` | `varchar(200)` | e.g., "Google Chrome" |
| `process_name` | `varchar(100)` | e.g., "chrome.exe" - authoritative matching key; UNIQUE |
| `category` | `varchar(50)` | `browser`, `communication`, `development`, `office`, `design`, `productivity`, `other` |
| `publisher` | `varchar(200)` | e.g., "Google LLC" |
| `icon_url` | `varchar(500)` | App icon for HR admin UI display |
| `is_public` | `boolean` | True = visible to all HR admins in catalog browser |
| `is_productive_default` | `boolean` | Default productivity classification when no tenant override exists |
| `created_by_id` | `uuid` | FK -> platform_users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `feature_flags`

Global runtime feature flag definitions with default values and deterministic tenant rollout percentages.

| Column | Type | Notes |
|:-------|:-----|:------|
| `key` | `varchar(120)` | PK; machine-readable flag key |
| `description` | `text` | Nullable |
| `default_value` | `boolean` | Global default value |
| `rollout_percentage` | `int` | 0-100 deterministic tenant rollout percentage |
| `module_key` | `varchar(80)` | Nullable FK -> module_catalog(module_key) |
| `feature_key` | `varchar(120)` | Nullable FK -> module_features(feature_key) |
| `is_active` | `boolean` | Soft-deactivate flag without deleting history |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `feature_flag_overrides`

Per-tenant runtime flag exceptions, evaluated only after module entitlement and commercial feature inclusion pass.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `flag_key` | `varchar(120)` | FK -> feature_flags(key) |
| `tenant_id` | `uuid` | FK -> tenants |
| `value` | `boolean` | Override value for this tenant |
| `granted_by_id` | `uuid` | FK -> platform_users |
| `granted_at` | `timestamptz` | |
| `reason` | `text` | Nullable audit reason |

**Unique:** `(flag_key, tenant_id)`

### `legal_holds`

Blocks deletion of specific records while under legal/compliance hold; retention jobs must check this before purging.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `resource_type` | `varchar(50)` | Polymorphic |
| `resource_id` | `uuid` | Polymorphic |
| `reason` | `text` |  |
| `placed_by_id` | `uuid` | FK -> users |
| `placed_at` | `timestamptz` |  |
| `released_by_id` | `uuid` | FK -> users (nullable) |
| `released_at` | `timestamptz` | Nullable |

### `legal_document_versions`

Phase 1 canonical source for published and current legal document versions managed by Developer Platform Compliance Center. Published rows are immutable; correcting content requires a new version. This table is database-backed and is not replaced by application configuration.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `document_type` | `varchar(80)` | `terms`, `privacy_notice`, `activity_monitoring_notice`, `screenshot_notice`, `biometric_photo_consent`, or `marketing` |
| `version` | `varchar(50)` | Stable version shown to users and stored in `legal_acceptance_records.document_version` |
| `title` | `varchar(200)` | Display title |
| `content_url` | `varchar(500)` | Nullable while draft; stored document URL or rendered content reference; required for publication |
| `is_required` | `boolean` | Whether acceptance or acknowledgement is required |
| `block_scope` | `varchar(40)` | `dashboard`, `workpulse_collection`, `verification`, or `none` |
| `status` | `varchar(20)` | `draft`, `published`, or `archived` |
| `published_by_id` | `uuid` | Nullable FK -> platform_users; set when published |
| `published_at` | `timestamptz` | Nullable; set when published |
| `publish_reason` | `text` | Nullable while draft; required when publishing |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | NOT NULL |

**Constraints and indexes:**
- Unique `(document_type, version)`.
- At most one row per `document_type` may have `status = published`; publishing a replacement archives the previously published row for that type.
- Index `(document_type, status, is_required, published_at DESC)` for current-version enforcement.

**Phase 1 legal-type rule:**
- `terms` and `privacy_notice` are platform-required current versions and use `block_scope = dashboard`.
- `activity_monitoring_notice` is required when the affected WorkPulse collection is enabled and uses `block_scope = workpulse_collection`.
- `screenshot_notice` and `biometric_photo_consent` are conditional collection/verification requirements.
- `marketing` is optional and uses `block_scope = none`.

`legal_acceptance_records` stores the tenant user's decision for the exact `(document_type, version)` pair. A missing or non-current required Terms or Privacy acceptance blocks normal tenant session issuance. A newly published platform-access version also invalidates normal platform access for existing sessions at their next authenticated validation; the old session is revoked before a limited pending-legal context is issued.

### `notifications`

In-app notification records (Notifications module owns delivery behavior; Shared Platform owns the physical table).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `recipient_user_id` | `uuid` | FK -> users |
| `category` | `varchar(40)` | e.g., `time_off`, `monitoring`, `discrepancy`, `system` |
| `type` | `varchar(80)` | e.g., `time_off.approved`, `monitoring.app_violation`, `verification.failed` |
| `title` | `varchar(200)` | |
| `message` | `text` | |
| `severity` | `varchar(20)` | `info`, `warning`, `critical` |
| `delivery_surface` | `varchar(30)` | `bell`, `inbox`, `email`, `signalr` |
| `related_entity_type` | `varchar(80)` | Nullable - polymorphic resource type |
| `related_entity_id` | `uuid` | Nullable - polymorphic resource id |
| `action_required` | `boolean` | Whether the recipient must take action |
| `is_read` | `boolean` | |
| `read_at` | `timestamptz` | Nullable |
| `resolved_at` | `timestamptz` | Nullable; set only when required action is resolved |
| `resolved_by_id` | `uuid` | FK -> users, nullable for system resolution |
| `resolution_note` | `text` | Nullable resolution explanation |
| `created_at` | `timestamptz` | |

### `notification_channels`

Tenant delivery channel configuration (email/push provider credentials, encrypted) used when dispatching notifications.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `channel_type` | `varchar(30)` | `email`, `push`, `slack` (Phase 2) |
| `provider` | `varchar(50)` | `resend`, `fcm`, `slack_webhook` (Phase 2) |
| `credentials_encrypted` | `jsonb` | Encrypted API keys/tokens |
| `is_active` | `boolean` |  |
| `configured_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `notification_templates`

Localized, versioned message templates per channel, rendered when notifications and emails are sent.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `template_code` | `varchar(50)` | e.g., `time_off_requested`, `payroll_completed` |
| `channel` | `varchar(20)` | `email`, `push`, `in_app` |
| `subject_template` | `text` | For email subject line |
| `body_template` | `text` | Handlebars/Liquid template |
| `locale` | `varchar(10)` | e.g., `en`, `si`, `ta` |
| `version` | `integer` |  |
| `is_active` | `boolean` |  |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `email_delivery_logs`

Email-only delivery/audit log for Resend-backed transactional email.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `notification_template_id` | `uuid` | Nullable FK -> notification_templates |
| `notification_channel_id` | `uuid` | Nullable FK -> notification_channels |
| `recipient_email` | `varchar(255)` | Recipient address used for the attempt |
| `subject_snapshot` | `varchar(500)` | Rendered subject at send time |
| `provider` | `varchar(50)` | `resend` |
| `provider_message_id` | `varchar(255)` | Nullable until provider accepts the message |
| `provider_event_id` | `varchar(255)` | Nullable; webhook event id for idempotency |
| `status` | `varchar(30)` | `queued`, `sent`, `delivered`, `bounced`, `failed`, `complained` |
| `attempt_count` | `integer` | Number of send attempts |
| `last_error` | `text` | Nullable provider or rendering error |
| `sent_at` | `timestamptz` | Nullable |
| `delivered_at` | `timestamptz` | Nullable |
| `bounced_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `support_tickets`

Tenant support tickets created by tenant users and worked by Developer Platform support agents.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `subject` | `varchar(200)` | NOT NULL |
| `description` | `text` | NOT NULL |
| `category` | `varchar(80)` | NOT NULL |
| `priority` | `varchar(20)` | NOT NULL |
| `status` | `varchar(30)` | `open`, `in_progress`, `waiting_for_customer`, `resolved` |
| `created_by_user_id` | `uuid` | FK -> users |
| `assigned_to_id` | `uuid` | Nullable FK -> platform_users |
| `last_customer_reply_at` | `timestamptz` | Nullable |
| `last_platform_reply_at` | `timestamptz` | Nullable |
| `last_activity_at` | `timestamptz` | NOT NULL |
| `resolved_by_id` | `uuid` | Nullable FK -> platform_users |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | NOT NULL |
| `resolved_at` | `timestamptz` | Nullable |

### `support_ticket_messages`

Customer-visible conversation thread on a ticket between tenant users and platform support.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `support_ticket_id` | `uuid` | FK -> support_tickets |
| `tenant_id` | `uuid` | FK -> tenants |
| `sender_type` | `varchar(30)` | `tenant_user`, `platform_user`, `system` |
| `sender_user_id` | `uuid` | Nullable FK -> users; required when `sender_type = tenant_user` |
| `sender_platform_user_id` | `uuid` | Nullable FK -> platform_users; required when `sender_type = platform_user` |
| `message_body` | `text` | NOT NULL |
| `message_format` | `varchar(20)` | `text`, `markdown` |
| `is_customer_visible` | `boolean` | Always true for tenant/platform replies returned to tenant APIs |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | Nullable |
| `edited_at` | `timestamptz` | Nullable |
| `deleted_at` | `timestamptz` | Nullable; soft delete |

### `support_ticket_internal_notes`

Platform-only notes; never returned by tenant-facing support APIs.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `support_ticket_id` | `uuid` | FK -> support_tickets |
| `tenant_id` | `uuid` | FK -> tenants |
| `author_platform_user_id` | `uuid` | FK -> platform_users |
| `note_body` | `text` | NOT NULL |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | Nullable |
| `edited_at` | `timestamptz` | Nullable |
| `deleted_at` | `timestamptz` | Nullable; soft delete |

### `support_ticket_events`

Activity timeline of ticket lifecycle changes (created, assigned, replied, resolved...) for audit and the ticket history view.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `support_ticket_id` | `uuid` | FK -> support_tickets |
| `tenant_id` | `uuid` | FK -> tenants |
| `event_type` | `varchar(80)` | e.g., `ticket.created`, `ticket.assigned`, `ticket.reply_added`, `ticket.status_changed`, `ticket.resolved` |
| `actor_type` | `varchar(30)` | `tenant_user`, `platform_user`, `system` |
| `actor_user_id` | `uuid` | Nullable FK -> users |
| `actor_platform_user_id` | `uuid` | Nullable FK -> platform_users |
| `old_values_json` | `jsonb` | Nullable previous state snapshot |
| `new_values_json` | `jsonb` | Nullable new state snapshot |
| `metadata_json` | `jsonb` | Nullable safe non-secret metadata |
| `created_at` | `timestamptz` | NOT NULL |

### `payment_methods`

Tenant saved payment instruments - only display metadata (brand, last four) plus the gateway's tokenized reference; no raw card data.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `type` | `varchar(20)` | `card`, `bank_transfer` |
| `last_four` | `varchar(4)` | Last 4 digits |
| `brand` | `varchar(20)` | `visa`, `mastercard`, etc. |
| `expiry_month` | `integer` |  |
| `expiry_year` | `integer` |  |
| `is_default` | `boolean` |  |
| `payment_provider_ref` | `varchar(100)` | Gateway payment method ID from Stripe, Paddle, or PayHere |
| `created_at` | `timestamptz` |  |

### `payment_gateway_configs`

Gateway metadata for Stripe, Paddle, PayHere (secrets live in `payment_gateway_credentials`).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `gateway_key` | `varchar(80)` | UNIQUE NOT NULL - operator-set readable slug, e.g. `'paddle-global-prod'` |
| `provider` | `varchar(30)` | FK -> platform_providers(provider_key); active `payment_gateway` family only |
| `environment` | `varchar(20)` | `sandbox`, `production` |
| `display_name` | `varchar(100)` | Friendly operator label |
| `logo_url` | `varchar(500)` | Nullable gateway logo |
| `public_key` | `varchar(255)` | Nullable; public identifier/key where applicable |
| `merchant_id` | `varchar(100)` | Nullable; Paddle seller ID or PayHere merchant ID |
| `webhook_url` | `varchar(500)` | Gateway callback/notify URL |
| `is_active` | `boolean` | Whether this config can be used for payment collection |
| `created_by_id` | `uuid` | FK -> platform_users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `payment_gateway_credentials`

Encrypted payment credentials; new secret = new row, old rows deactivated.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `payment_gateway_config_id` | `uuid` | FK -> payment_gateway_configs |
| `secret_encrypted` | `bytea` | Encrypted Stripe secret key, Paddle API key, or PayHere merchant secret |
| `webhook_secret_encrypted` | `bytea` | Encrypted webhook/notify secret when separate |
| `encryption_key_version` | `varchar(50)` | Key version used by `IEncryptionService` |
| `credential_version` | `integer` | Monotonic version per gateway config |
| `is_active` | `boolean` | Only the active row may be used for provider calls |
| `rotated_by_id` | `uuid` | FK -> platform_users |
| `rotated_at` | `timestamptz` | When this credential version was added |
| `deactivated_by_id` | `uuid` | Nullable FK -> platform_users |
| `deactivated_at` | `timestamptz` | Nullable |

### `payment_gateway_country_routes`

Country-to-gateway routing for subscription and invoice collection.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `country_code` | `varchar(2)` | ISO 3166-1 alpha-2 |
| `country_name_snapshot` | `varchar(120)` | Display snapshot for audit/readability |
| `gateway_config_id` | `uuid` | FK -> payment_gateway_configs |
| `environment` | `varchar(20)` | `sandbox`, `production` |
| `is_active` | `boolean` | Whether this route can be used |
| `created_by_id` | `uuid` | FK -> platform_users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `webhook_event_queue`

Reliable event processing queue for inbound Stripe, Paddle, and PayHere webhooks. Ensures at-least-once processing with dead-letter tracking.

| Column | Type | Notes |
|:-------|:-----|:------|
| `provider` | `varchar(20)` | NOT NULL - `stripe`, `paddle`, or `payhere` |
| `event_id` | `varchar(100)` | UNIQUE, NOT NULL - provider event/order ID; idempotency key |
| `event_type` | `varchar(100)` | NOT NULL - e.g. `payment_intent.succeeded` |
| `payload` | `jsonb` | NOT NULL - full webhook payload |
| `status` | `varchar(20)` | `pending`, `processing`, `completed`, `failed`, `dead_letter` |
| `attempt_count` | `integer` | NOT NULL, default 0 |
| `last_attempt_at` | `timestamptz` | Nullable |
| `next_retry_at` | `timestamptz` | Nullable - exponential backoff schedule |
| `error_message` | `text` | Nullable - last processing error |
| `received_at` | `timestamptz` | NOT NULL |
| `completed_at` | `timestamptz` | Nullable |

**Index:** `(status, next_retry_at)`, `UNIQUE(provider, event_id)`

### `subscription_plans`

Reusable plan catalog - module bundles, feature limits, and employee-count pricing tiers that tenant subscriptions are built from.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `name` | `varchar(100)` |  |
| `code` | `varchar(20)` | `starter`, `professional`, `enterprise` |
| `tier` | `varchar(20)` | Ordering tier |
| `feature_limits` | `jsonb` | e.g., `{"max_employees": 50, "modules": ["core_hr","time_off"]}` |
| `included_modules` | `jsonb` | Plan-allowed/included module keys used by entitlement resolution |
| `price_tiers` | `jsonb` | Employee-count pricing tiers/rate table |
| `pricing_unit` | `varchar(30)` | `per_employee`, `per_device`, `flat`, `custom` |
| `calculated_monthly_price` | `decimal(10,2)` | Sum of selected module bracket monthly prices |
| `calculated_annual_price` | `decimal(10,2)` | Sum of selected module bracket annual prices |
| `override_monthly_price` | `decimal(10,2)` | Nullable operator-adjusted monthly price |
| `override_annual_price` | `decimal(10,2)` | Nullable operator-adjusted annual price |
| `ai_token_limit_per_month` | `integer` | Nullable; required positive cap when the plan includes AI entitlement |
| `currency` | `varchar(3)` | ISO 4217 |
| `is_active` | `boolean` |  |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `plan_features`

Feature and limit rows composing a subscription plan (e.g., max employees, included modules).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `plan_id` | `uuid` | FK -> subscription_plans |
| `feature_key` | `varchar(100)` | e.g., `payroll`, `activity_monitoring` |
| `limit_value` | `integer` | Nullable - null means unlimited |
| `is_included` | `boolean` |  |

### `subscription_plan_price_history`

Audit trail of plan catalog price changes so historical pricing decisions are preserved without rewriting tenant contracts.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `plan_id` | `uuid` | FK -> subscription_plans |
| `old_monthly_price` | `decimal(10,2)` | Nullable |
| `new_monthly_price` | `decimal(10,2)` | Nullable |
| `old_annual_price` | `decimal(10,2)` | Nullable |
| `new_annual_price` | `decimal(10,2)` | Nullable |
| `old_currency` | `varchar(3)` | Nullable |
| `new_currency` | `varchar(3)` | ISO 4217 |
| `old_pricing_unit` | `varchar(30)` | Nullable |
| `new_pricing_unit` | `varchar(30)` | `per_employee`, `per_device`, `flat`, `custom` |
| `changed_by_id` | `uuid` | FK -> platform_users |
| `reason` | `text` | Required business reason |
| `changed_at` | `timestamptz` | |

### `module_catalog`

Global catalog for ONEVO modules.

| Column | Type | Notes |
|:-------|:-----|:------|
| `module_key` | `varchar(100)` | PK; e.g., `core_hr`, `time_off`, `work_management` |
| `name` | `varchar(150)` | Display name |
| `pillar` | `varchar(50)` | Product grouping only |
| `phase` | `varchar(30)` | `phase_1`, `phase_2`, `future`, or product-defined release phase |
| `pricing_reference` | `jsonb` | Company-size pricing reference only |
| `storage_reference` | `jsonb` | Company-size storage reference only |
| `ai_token_reference` | `jsonb` | Company-size AI token reference only |
| `pricing_unit` | `varchar(30)` | `per_employee`, `per_user`, `flat`, `custom` |
| `is_ai_enabled` | `boolean` | True when AI token references are relevant |
| `is_storage_consuming` | `boolean` | True when storage references are relevant |
| `is_active` | `boolean` | Whether the module can be selected for new plans |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `module_features`

Commercial feature registry inside a module.

| Column | Type | Notes |
|:-------|:-----|:------|
| `feature_key` | `varchar(120)` | PK; format `{module_key}.{feature_name}` |
| `module_key` | `varchar(100)` | FK -> module_catalog.module_key |
| `name` | `varchar(150)` | Display name |
| `description` | `text` | Nullable |
| `is_default_included` | `boolean` | Selected by default when the module is added to a plan |
| `is_active` | `boolean` | Whether this feature can be selected for new plans/contracts |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `integration_catalog`

Operator-managed catalog of connectable software integrations shown in the tenant app. Stores metadata only; it must not store provider secrets, tenant tokens, or employee tokens. Resend, Cloudflare, Stripe, Paddle, PayHere, and biometric terminals are not Integration Catalog entries.

| Column | Type | Notes |
|:-------|:-----|:------|
| `integration_key` | `varchar(50)` | PK; operator-set slug, e.g. `github`, `zoom`, `google_calendar` |
| `display_name` | `varchar(100)` | NOT NULL |
| `description` | `text` | Nullable |
| `connection_scope` | `varchar(20)` | `tenant`, `user`, or `both`; describes supported connection layers, not token storage by itself |
| `onevo_app_provider` | `varchar(30)` | FK -> platform_oauth_apps.provider; ONEVO OAuth app registration used for consent |
| `logo_url` | `varchar(500)` | Nullable |
| `is_active` | `boolean` | NOT NULL |
| `created_by_id` | `uuid` | FK -> platform_users(id) |
| `created_at` | `timestamptz` | NOT NULL |

### `module_integration_links`

Links integration catalog entries to ONEVO product modules. Controls which integrations become visible/connectable when a tenant has the related module entitlement. This table stores visibility linkage only; it does not store credentials or tokens.

| Column | Type | Notes |
|:-------|:-----|:------|
| `module_key` | `varchar(80)` | FK -> module_catalog(module_key) |
| `integration_key` | `varchar(50)` | FK -> integration_catalog(integration_key) |
| `linked_by_id` | `uuid` | FK -> platform_users(id) |
| `linked_at` | `timestamptz` | NOT NULL |

**PK:** `(module_key, integration_key)`

### `tenant_integration_credentials`

Per-tenant approval, configuration, connection state, and tokens only for true tenant-owned integrations. For a provider with both layers, such as GitHub, this row may represent tenant enable/disable/configuration state, but a user's personal OAuth token must not be stored here. User-level Google/Outlook Calendar tokens stay in `external_calendar_connections`; other user-owned OAuth tokens stay in `user_integration_connections`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `integration_key` | `varchar(50)` | FK -> integration_catalog(integration_key) |
| `access_token_encrypted` | `text` | Nullable; AES-256 encrypted |
| `refresh_token_encrypted` | `text` | Nullable; AES-256 encrypted |
| `token_expires_at` | `timestamptz` | Nullable |
| `scopes_granted` | `text[]` | Scopes the customer authorised during OAuth |
| `external_account_id` | `varchar(200)` | Nullable; GitHub org ID, Microsoft tenant ID, Google workspace ID, etc. |
| `external_account_name` | `varchar(200)` | Nullable human-readable connected account name |
| `status` | `varchar(20)` | `connected`, `error`, `expired`, `disconnected`, `disabled`; `connected` is tenant-enabled/approved, `disconnected` is an explicit tenant-admin disable, and `disabled` is temporary module/runtime suppression |
| `last_sync_at` | `timestamptz` | Nullable |
| `error_message` | `text` | Nullable last provider error |
| `connected_at` | `timestamptz` | NOT NULL |
| `connected_by_user_id` | `uuid` | FK -> users(id) |
| `disconnected_at` | `timestamptz` | Nullable |

**Unique:** `(tenant_id, integration_key)`; **Index:** `(tenant_id, status)`, `(integration_key)`

For GitHub, tenant approval uses this row with null token fields. Personal GitHub tokens are never stored here. Own-user OAuth is permitted only while the row is `connected`; `disabled`, `disconnected`, missing, `error`, and `expired` rows deny new start, callback, and refresh operations.

### `user_integration_connections`

Per-user OAuth connection state and provider identity metadata for generic user-owned integrations such as GitHub, Zoom, and Microsoft Teams user identity. This table is separate from tenant approval/configuration in `tenant_integration_credentials` and from calendar-specific OAuth and sync state in `external_calendar_connections`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | NOT NULL; FK -> tenants(id) |
| `user_id` | `uuid` | NOT NULL; FK -> users(id) |
| `integration_key` | `varchar(50)` | NOT NULL; FK -> integration_catalog(integration_key) |
| `provider_user_id` | `varchar(200)` | Nullable |
| `provider_username` | `varchar(200)` | Nullable |
| `provider_email` | `varchar(320)` | Nullable |
| `access_token_encrypted` | `text` | Nullable; encrypted server-side |
| `refresh_token_encrypted` | `text` | Nullable; encrypted server-side |
| `token_expires_at` | `timestamptz` | Nullable |
| `scopes_granted` | `text[]` | Nullable |
| `status` | `varchar(20)` | NOT NULL; `connected`, `error`, `expired`, `disconnected` |
| `last_used_at` | `timestamptz` | Nullable |
| `last_sync_at` | `timestamptz` | Nullable |
| `error_message` | `text` | Nullable |
| `connected_at` | `timestamptz` | NOT NULL |
| `disconnected_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | Nullable |

**Constraints and indexes:**
- Unique active connection on `(tenant_id, user_id, integration_key)` where `disconnected_at IS NULL`.
- Index on `(tenant_id, user_id)`.
- Index on `(tenant_id, integration_key)`.
- Index on `status`.
- Tokens must be encrypted server-side. Raw or encrypted token values, authorization codes, client secrets, and provider raw responses must never be logged or returned by an API.
- GitHub personal OAuth tokens are stored here, never in `tenant_integration_credentials` or `external_calendar_connections`.
- `external_calendar_connections` remains the user-level Google/Outlook Calendar table because it also owns calendar sync tokens, delta links, and event-sync state.

### `module_permission_ownership`

Exclusive ownership map between product modules and seeded tenant-facing permission codes.

| Column | Type | Notes |
|:-------|:-----|:------|
| `module_key` | `varchar(100)` | FK -> module_catalog.module_key |
| `permission_code` | `varchar(120)` | FK -> permissions/code catalog |
| `is_default_permission` | `boolean` | Included in future tenant Owner role materialization |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**PK:** `(module_key, permission_code)`; **Unique:** `(permission_code)`

### `module_catalog_price_history`

Audit trail of module catalog pricing/storage/AI reference changes, with required business reason.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `module_key` | `varchar(100)` | FK -> module_catalog.module_key |
| `old_pricing_reference` | `jsonb` | Nullable previous company-size pricing references |
| `new_pricing_reference` | `jsonb` | Nullable new company-size pricing references |
| `old_storage_reference` | `jsonb` | Nullable previous storage references |
| `new_storage_reference` | `jsonb` | Nullable new storage references |
| `old_ai_token_reference` | `jsonb` | Nullable previous AI token references |
| `new_ai_token_reference` | `jsonb` | Nullable new AI token references |
| `old_pricing_unit` | `varchar(30)` | Nullable |
| `new_pricing_unit` | `varchar(30)` | `per_employee`, `per_device`, `flat`, `custom` |
| `changed_by_id` | `uuid` | FK -> platform_users |
| `reason` | `text` | Required business reason |
| `changed_at` | `timestamptz` | |

### `tenant_module_entitlements`

Tenant-level module entitlement records derived from the active subscription plan and add-ons.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `module_key` | `varchar(100)` | FK -> module_catalog.module_key |
| `sales_state` | `varchar(30)` | `subscription_included`, `purchased`, `quoted`, `available`, or `disabled` |
| `runtime_override` | `boolean` | Nullable runtime-only override; `NULL` inherits, `false` force-disables, `true` restores runtime access |
| `pricing_model` | `varchar(30)` | `subscription`, `addon`, or `custom` |
| `price` | `decimal(12,2)` | Nullable override price |
| `currency` | `varchar(3)` | ISO 4217 |
| `starts_at` | `date` | Nullable entitlement start |
| `ends_at` | `date` | Nullable entitlement/subscription end |
| `created_by_user_id` | `uuid` | Nullable FK -> users; tenant self-service entitlement creation |
| `created_by_platform_user_id` | `uuid` | Nullable FK -> platform_users; platform-operator entitlement creation |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `tenant_subscriptions`

A tenant's actual subscription contract - selected plan, billing cycle, module/resource add-ons, calculated vs override prices, and resolved storage/AI limits.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `allowed_plan_ids` | `jsonb` | Plan IDs the tenant may choose from during demo upgrade or onboarding |
| `recommended_plan_id` | `uuid` | Nullable FK -> subscription_plans |
| `plan_id` | `uuid` | Nullable FK -> subscription_plans; final selected plan |
| `billing_cycle` | `varchar(20)` | `monthly` or `annual` |
| `status` | `varchar(30)` | `pending_payment`, `active`, `past_due`, `cancelled` |
| `current_period_start` | `date` | |
| `current_period_end` | `date` | |
| `billing_currency` | `varchar(3)` | ISO 4217 |
| `confirmed_employee_count` | `integer` | Used for first invoice quantity and company-size bracket |
| `selected_base_modules` | `jsonb` | Snapshot from selected plan |
| `selected_addon_modules` | `jsonb` | Selected optional module add-ons |
| `selected_resource_addons` | `jsonb` | Selected storage/AI resource packs |
| `calculated_monthly_price` | `decimal(10,2)` | Snapshot of calculated monthly amount |
| `calculated_annual_price` | `decimal(10,2)` | Snapshot of calculated annual amount |
| `annual_price_override` | `decimal(10,2)` | Nullable explicit annual override |
| `annual_discount_percent` | `decimal(5,2)` | Nullable annual discount |
| `ai_token_limit_per_month` | `integer` | Resolved shared AI token allowance |
| `payment_gateway_config_id` | `uuid` | FK -> payment_gateway_configs; resolved from tenant country route |
| `unpaid_seat_dues_amount` | `decimal(12,2)` | Blocks cancellation/renewal changes when greater than zero |
| `created_by_user_id` | `uuid` | Nullable FK -> users; tenant self-service |
| `created_by_platform_user_id` | `uuid` | Nullable FK -> platform_users; platform-operator |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### `tenant_subscription_events`

Commercial subscription change history.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_subscription_id` | `uuid` | FK -> tenant_subscriptions |
| `tenant_id` | `uuid` | FK -> tenants |
| `event_type` | `varchar(80)` | e.g. `created`, `plan_changed`, `addons_changed`, `billing_cycle_changed`, `cancel_requested`, `platform_adjusted` |
| `actor_user_id` | `uuid` | Nullable FK -> users |
| `actor_platform_user_id` | `uuid` | Nullable FK -> platform_users |
| `old_values_json` | `jsonb` | Nullable previous commercial snapshot |
| `new_values_json` | `jsonb` | New commercial snapshot |
| `reason` | `text` | Nullable tenant reason or required platform operator reason |
| `created_at` | `timestamptz` | |

### `tenant_status_histories`

Admin/platform lifecycle audit trail for tenant status transitions (`provisioning`, `trial`, `trial_expired`, `pending_payment`, `active`, `suspended`, `cancelled`). Written by the admin status-change endpoint (suspend/unsuspend/activate/cancel) and by provisioning confirmation (`provisioning` -> `trial`). This is the audit source for who changed a tenant's status, from what to what, and why.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `from_status` | `varchar(20)` | Required; status before the change |
| `to_status` | `varchar(20)` | Required; status after the change |
| `reason` | `text` | Nullable; required by validation for `suspend`, optional otherwise |
| `changed_by_id` | `uuid` | Nullable FK -> platform_users; the platform admin who made the change. Every Phase 1 writer is an authenticated platform admin action - null is reserved for a future automated/system-initiated transition, none of which exist yet |
| `changed_at` | `timestamptz` | Required |

**Indexes:** `tenant_id`; `changed_at`. A composite `(tenant_id, changed_at)` index is deferred until a read/query API is built.

**Constraints:** `tenant_id` FK -> `tenants.id` (`ON DELETE RESTRICT`, tenant-owned dependent convention, matches `tenant_storage_stats.tenant_id` and `mfa_challenges.tenant_id`). `changed_by_id` FK -> `platform_users.id`, nullable (`ON DELETE SET NULL`, matches `platform_auth_events.user_id`).

**Read API:** not built in Phase 1. Likely future endpoint: `GET /admin/v1/tenants/{id}/status-history`, gated by `TenantsRead` or `TenantsManage` (decision deferred).

### `billing_audit_logs`

Immutable append-only audit trail for billing mutations. No UPDATE or DELETE is permitted on this table.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `actor_id` | `uuid` | Nullable FK -> platform_users; NULL means system action |
| `actor_type` | `varchar(20)` | `platform_admin` or `system` |
| `action` | `varchar(80)` | e.g. `invoice.marked_paid`, `subscription.overridden`, `tenant.auto_suspended_dunning` |
| `entity_type` | `varchar(40)` | `invoice`, `subscription`, `gateway`, `tenant` |
| `entity_id` | `uuid` | PK of the affected row |
| `old_value` | `jsonb` | Nullable previous state snapshot |
| `new_value` | `jsonb` | New state snapshot |
| `reason` | `text` | Required for admin actions; system actions use fixed reason string |
| `created_at` | `timestamptz` | NOT NULL |

**Index:** `(tenant_id, created_at DESC)`, `(entity_type, entity_id)`, `(actor_id)`

### `subscription_invoices`

Billing invoices raised against a tenant subscription, linked to the gateway used for collection.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `subscription_id` | `uuid` | FK -> tenant_subscriptions |
| `payment_gateway_config_id` | `uuid` | FK -> payment_gateway_configs |
| `invoice_number` | `varchar(50)` |  |
| `amount` | `decimal(10,2)` |  |
| `currency` | `varchar(3)` |  |
| `status` | `varchar(20)` | `draft`, `open`, `paid`, `void` |
| `external_invoice_id` | `varchar(100)` | Gateway invoice ID from Stripe, Paddle, or PayHere |
| `issued_at` | `timestamptz` |  |
| `paid_at` | `timestamptz` | Nullable |

### `billing_snapshots`

End-of-month snapshot of billable units per tenant.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `snapshot_date` | `date` | Last day of the billing month |
| `active_employee_count` | `integer` | Active employees at snapshot time |
| `enrolled_device_count` | `integer` | Enrolled devices at snapshot time |
| `employee_breakdown` | `jsonb` | Count by department |
| `device_breakdown` | `jsonb` | Count by department |
| `created_at` | `timestamptz` | |

**Unique:** `(tenant_id, snapshot_date)`

### `tenant_provisioning_states`

Draft-safe provisioning wizard state.

| Column | Type | Notes |
|:-------|:-----|:------|
| `tenant_id` | `uuid` | PK, FK -> tenants |
| `current_step` | `varchar(50)` | `tenant_profile`, `subscription`, `modules`, `roles`, `settings`, `owner_invite`, `review` |
| `tenant_details_completed_at` | `timestamptz` | Nullable |
| `subscription_completed_at` | `timestamptz` | Nullable |
| `modules_completed_at` | `timestamptz` | Nullable |
| `roles_completed_at` | `timestamptz` | Nullable |
| `settings_completed_at` | `timestamptz` | Nullable |
| `owner_invite_completed_at` | `timestamptz` | Nullable |
| `activation_ready` | `boolean` | Cached readiness after latest validation |
| `activated_at` | `timestamptz` | Nullable |
| `last_updated_by_id` | `uuid` | FK -> platform_users |
| `updated_at` | `timestamptz` | |

### `tenant_provisioning_validation_results`

Latest activation blockers and warnings from provisioning validation - what still stands between a draft tenant and activation.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `section` | `varchar(50)` | Provisioning section |
| `code` | `varchar(100)` | Machine-readable validation code |
| `message` | `text` | Human-readable message |
| `severity` | `varchar(20)` | `blocker` or `warning` |
| `resolved_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` | |

### `configuration_templates`

Reusable provisioning and configuration templates managed in Developer Platform -> Configuration Template Manager. Templates may be selected or recommended from the tenant's company-size range, industry profile, country/legal context, entitled modules, and requested template type. This is the only canonical reusable provisioning-template definition model; it is not a billing or charge model.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `template_key` | `varchar(100)` | Unique machine-readable key, e.g. `uk-standard-time_off`, `engineering-positions` |
| `template_type` | `varchar(50)` | `configuration`, `position_template`, `time_off_policy`, `onboarding`, `app_allowlist`, `monitoring_policy`, `data_import_mapping` |
| `name` | `varchar(150)` | Display name |
| `description` | `varchar(500)` | Nullable - shown in the template picker |
| `version` | `integer` | Incremented on every edit |
| `module_keys_json` | `jsonb` | Module keys that must be entitled on the tenant before apply is allowed |
| `industry_profile_tag` | `varchar(50)` | Nullable - links monitoring policy templates to an industry |
| `payload_json` | `jsonb` | Type-specific template content |
| `is_system` | `boolean` | `true` = ONEVO-managed default; cannot be edited, only cloned |
| `is_active` | `boolean` | Inactive templates cannot be applied |
| `created_by_id` | `uuid` | FK -> platform_users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Position template meaning:** `position_template` stores reusable position and organization-structure configuration payloads. It is not a billing type and does not represent tenant selection state.

### `tenant_configuration_template_applications`

Immutable audit record of every template application to a tenant; one row is written for every apply action. The row snapshots the applied version and payload, retains warnings, and never represents a billing charge.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `configuration_template_id` | `uuid` | FK -> configuration_templates |
| `template_type` | `varchar(50)` | Snapshot of the template type at apply time |
| `applied_version` | `integer` | Template version that was applied |
| `applied_payload_json` | `jsonb` | Snapshot of the payload that was applied - immutable after creation |
| `custom_payload_json` | `jsonb` | Nullable - snapshot of tenant-specific overrides supplied with this apply action; immutable after creation |
| `warnings_json` | `jsonb` | Nullable apply-time warnings retained for audit |
| `status` | `varchar(20)` | Apply-time result; `applied` for a successful application and immutable after creation |
| `applied_by_id` | `uuid` | FK -> platform_users |
| `applied_at` | `timestamptz` | |

**Rule:** Application history is append-only. Editing tenant configuration does not update an application row, and reapplying a template creates a new row while preserving every prior row unchanged.

### `rate_limit_rules`

Per-endpoint API rate limits (sliding window), definable globally or per tenant.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants (nullable - null for global rules) |
| `endpoint_pattern` | `varchar(200)` | e.g., `/api/v1/time-off/*` |
| `key_scope` | `varchar(40)` | `tenant`, `user`, `ip`, or `ip_normalized_email` |
| `max_requests` | `integer` | Per window |
| `window_seconds` | `integer` | Sliding window size |
| `is_active` | `boolean` |  |
| `created_at` | `timestamptz` |  |

**Constraints:** `max_requests > 0`; `window_seconds > 0`; partial UNIQUE `(endpoint_pattern, key_scope) WHERE tenant_id IS NULL AND is_active = true`; partial UNIQUE `(tenant_id, endpoint_pattern, key_scope) WHERE tenant_id IS NOT NULL AND is_active = true`.

**Base-login seeds:** Exact endpoint pattern `POST:/api/v1/auth/login:base` has two required global active rows: `key_scope = ip_normalized_email`, `max_requests = 5`, `window_seconds = 300`; and `key_scope = ip`, `max_requests = 20`, `window_seconds = 300`. Missing or conflicting protection fails closed with `503 login_protection_unavailable` before candidate lookup or BCrypt. Exceeding either bucket returns generic `429` with `Retry-After`.

### `retention_policies`

How long each data type is kept per tenant and what happens on expiry (delete/anonymize/archive), tied to a compliance framework.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `resource_type` | `varchar(50)` |  |
| `retention_days` | `integer` |  |
| `action_on_expiry` | `varchar(30)` | `delete`, `anonymize`, `archive` |
| `compliance_framework` | `varchar(50)` | e.g., `GDPR`, `local_labor_law` |
| `is_active` | `boolean` |  |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |

### `scheduled_tasks`

Registered background job schedules (cron) per tenant or system-wide, with last/next run tracking.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants (nullable - null for system tasks) |
| `task_type` | `varchar(100)` | Job class name |
| `cron_expression` | `varchar(50)` |  |
| `description` | `text` |  |
| `is_active` | `boolean` |  |
| `last_run_at` | `timestamptz` | Nullable |
| `next_run_at` | `timestamptz` |  |
| `created_at` | `timestamptz` |  |

### `signalr_connections`

Live registry of active SignalR connections per user/device, used to target real-time notification delivery.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `user_id` | `uuid` | FK -> users |
| `tenant_id` | `uuid` | FK -> tenants |
| `connection_id` | `varchar(100)` | SignalR connection ID |
| `channel` | `varchar(30)` | `web`, `mobile`, `desktop_agent` |
| `device_type` | `varchar(30)` | `browser`, `ios`, `android`, `windows` |
| `connected_at` | `timestamptz` |  |
| `last_ping_at` | `timestamptz` |  |
| `is_active` | `boolean` |  |

### `sso_providers`

Tenant SSO login provider configuration (Google only in Phase 1), with encrypted client credentials and auto-provisioning behavior.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `provider_type` | `varchar(30)` | Phase 1: `google` only. Future: `saml`, `oidc`. Microsoft Teams is not an SSO provider in Phase 1 |
| `name` | `varchar(100)` | Display name |
| `client_id_encrypted` | `bytea` | Encrypted via IEncryptionService |
| `client_secret_encrypted` | `bytea` | Encrypted via IEncryptionService |
| `metadata_url` | `varchar(500)` | SAML metadata / OIDC discovery URL |
| `domain_hint` | `varchar(100)` | Auto-select provider by email domain |
| `auto_provision_users` | `boolean` | Create user on first SSO login |
| `is_active` | `boolean` |  |
| `created_at` | `timestamptz` |  |
| `updated_at` | `timestamptz` |  |

### `system_settings`

Global platform key-value settings (not tenant-scoped).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `setting_key` | `varchar(100)` | Unique |
| `setting_value` | `jsonb` |  |
| `description` | `text` |  |
| `updated_by_id` | `uuid` | FK -> users |
| `updated_at` | `timestamptz` |  |

### `tenant_branding`

Tenant white-label branding - logo and colors applied across the customer app.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `logo_file_id` | `uuid` | FK -> file_records (nullable) |
| `primary_color` | `varchar(7)` | Hex color |
| `accent_color` | `varchar(7)` | Hex color |
| `metadata` | `jsonb` | Additional branding config |
| `updated_by_id` | `uuid` | FK -> users |
| `updated_at` | `timestamptz` |  |

### `user_preferences`

Per-user platform preferences (theme, locale, dashboard layout, and personal notification delivery choices) as key-value rows.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `user_id` | `uuid` | FK -> users |
| `tenant_id` | `uuid` | FK -> tenants |
| `preference_key` | `varchar(100)` | e.g., `theme`, `locale`, `dashboard_layout`, `notifications.channels.time_off`, `notifications.quiet_hours`, `notifications.digest_mode` (employee display timezone lives on `employees.display_timezone`, not here) |
| `preference_value` | `jsonb` |  |
| `updated_at` | `timestamptz` |  |

Unique `(tenant_id, user_id, preference_key)`. Personal notification preferences are self-service; tenant defaults remain in `tenant_settings.settings_json`, and mandatory critical/security/legal delivery cannot be disabled.

### `webhook_endpoints`

Tenant-registered outbound webhook targets with HMAC signing secrets and subscribed event types.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `url` | `varchar(500)` | Target URL |
| `secret_hash` | `varchar(255)` | HMAC signing secret hash |
| `events` | `jsonb` | Subscribed event types |
| `is_active` | `boolean` |  |
| `created_by_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` |  |

### `webhook_deliveries`

Delivery attempts and responses per webhook event, for retry handling and debugging.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `webhook_endpoint_id` | `uuid` | FK -> webhook_endpoints |
| `event_type` | `varchar(50)` |  |
| `payload` | `jsonb` | Sent payload |
| `response_status` | `integer` | HTTP status code |
| `response_body` | `text` | Truncated response |
| `attempt_number` | `integer` | Retry count |
| `delivered_at` | `timestamptz` |  |

---

# Developer Platform (14 tables)

> Internal dev console (`console.onevo.io`), not tenant-scoped. Source: `developer-platform/database/schema.md`. Release/ring management tables and `platform_api_keys` are Phase 2 and remain outside this Phase 1 inventory.

### `platform_users`

Administrative identity, profile, and status records for the Developer Platform. Password hashes and password lifecycle state are not stored on this table.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `email` | `varchar(255)` | UNIQUE, NOT NULL |
| `full_name` | `varchar(255)` | NOT NULL |
| `google_sub` | `varchar(255)` | Nullable Google OAuth subject identifier |
| `status` | `varchar(20)` | `pending`, `active`, or `inactive` |
| `mfa_status` | `varchar(20)` | `not_enrolled`, `enrolled`, `locked`, or equivalent policy state |
| `invite_status` | `varchar(20)` | `pending`, `accepted`, `revoked`, `expired` |
| `created_by_id` | `uuid` | FK -> platform_users, nullable for seed Super Admin |
| `created_at` | `timestamptz` | NOT NULL |
| `last_login_at` | `timestamptz` | Nullable; last successful authentication |

### `platform_user_credentials`

Stores database-backed credential records for Developer Platform users. This table supports production platform/admin email-password authentication without putting password hashes or credential lifecycle state directly on `platform_users`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `platform_user_id` | `uuid` | NOT NULL, FK -> platform_users(id) |
| `credential_type` | `varchar(40)` | NOT NULL; Phase 1 allowed value: `password` |
| `password_hash` | `varchar(255)` | Nullable generally; required when credential_type = `password` |
| `password_algorithm` | `varchar(80)` | Nullable; algorithm/work-factor metadata |
| `password_changed_at` | `timestamptz` | Nullable |
| `must_change_password` | `boolean` | NOT NULL, default false |
| `failed_login_count` | `int` | NOT NULL, default 0 |
| `locked_until` | `timestamptz` | Nullable |
| `reset_token_hash` | `varchar(255)` | Nullable; never stores a plaintext reset token |
| `reset_token_expires_at` | `timestamptz` | Nullable |
| `last_used_at` | `timestamptz` | Nullable |
| `revoked_at` | `timestamptz` | Nullable; null means active |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | Nullable |

**Constraints:** `credential_type IN ('password')`; `password_hash IS NOT NULL` when `credential_type = 'password'`; only one active password credential per platform user.

**Indexes:** `btree(platform_user_id)`; partial unique index on `(platform_user_id, credential_type) WHERE revoked_at IS NULL`; `btree(reset_token_hash)` for reset-token lookup.

### `platform_user_invites`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `email` | `varchar(255)` | NOT NULL |
| `full_name` | `varchar(255)` | NOT NULL |
| `invite_token_hash` | `varchar(64)` | NOT NULL; raw token never stored |
| `invited_by_id` | `uuid` | FK -> platform_users, NOT NULL |
| `expires_at` | `timestamptz` | NOT NULL |
| `accepted_at` | `timestamptz` | Nullable |
| `revoked_at` | `timestamptz` | Nullable |
| `created_at` | `timestamptz` | NOT NULL |

### `platform_roles`

Role presets and custom roles for Developer Platform operators (separate from tenant RBAC).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `name` | `varchar(100)` | NOT NULL |
| `description` | `text` | Nullable |
| `is_system` | `boolean` | System roles can be cloned but not deleted |
| `is_active` | `boolean` | Default true |
| `created_by_id` | `uuid` | FK -> platform_users, nullable for seed roles |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | NOT NULL |

### `platform_permissions`

Platform-admin permission catalog (Developer Platform modules only; not tenant permissions).

| Column | Type | Notes |
|:-------|:-----|:------|
| `code` | `varchar(120)` | PK, e.g. `platform:tenants:read` |
| `module_key` | `varchar(80)` | Developer Platform module key |
| `description` | `text` | Nullable |
| `is_high_risk` | `boolean` | Marks permissions such as impersonation and account management |

### `platform_role_permissions`

Grants platform permissions to platform roles, with grantor audit.

| Column | Type | Notes |
|:-------|:-----|:------|
| `role_id` | `uuid` | FK -> platform_roles, NOT NULL |
| `permission_code` | `varchar(120)` | FK -> platform_permissions(code), NOT NULL |
| `granted_by_id` | `uuid` | FK -> platform_users, NOT NULL |
| `granted_at` | `timestamptz` | NOT NULL |

**PK:** `(role_id, permission_code)`

### `platform_user_roles`

Assigns platform roles to platform users - effective operator access comes from this mapping.

| Column | Type | Notes |
|:-------|:-----|:------|
| `user_id` | `uuid` | FK -> platform_users, NOT NULL |
| `role_id` | `uuid` | FK -> platform_roles, NOT NULL |
| `assigned_by_id` | `uuid` | FK -> platform_users, NOT NULL |
| `assigned_at` | `timestamptz` | NOT NULL |

**PK:** `(user_id, role_id)`

### `platform_user_sessions`

Sessions are created only after MFA succeeds; tokens hashed, never stored raw.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `account_id` | `uuid` | FK -> platform_users, NOT NULL |
| `token_hash` | `varchar(64)` | SHA-256 hash of session token, NOT NULL |
| `created_at` | `timestamptz` | NOT NULL |
| `expires_at` | `timestamptz` | NOT NULL; session TTL |
| `ip_address` | `varchar(45)` | Nullable; IPv4 or IPv6, for audit/security |

### `platform_auth_events`

Immutable authentication and access history for Developer Platform users.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `user_id` | `uuid` | Nullable FK -> platform_users; nullable for failed login before user resolution |
| `event_type` | `varchar(80)` | e.g. `login_succeeded`, `login_failed`, `mfa_succeeded`, `mfa_failed`, `password_reset_requested`, `password_reset_completed`, `session_revoked` |
| `source_ip` | `varchar(45)` | Nullable |
| `user_agent` | `text` | Nullable |
| `metadata_json` | `jsonb` | Safe structured context only; no passwords, tokens, or secrets |
| `created_at` | `timestamptz` | NOT NULL |

### `platform_alerts`

Platform and tenant-scoped operational/security alerts generated automatically by Developer Platform detection paths. Operators can acknowledge and resolve rows; alerts are not manually created.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `alert_code` | `varchar(80)` | Machine-readable code from the alert catalog |
| `severity` | `varchar(10)` | `critical`, `warning`, `info` |
| `tenant_id` | `uuid` | Nullable FK -> tenants; null for platform-level alerts |
| `source_module` | `varchar(80)` | Module key that raised the alert |
| `title` | `varchar(200)` | Human-readable summary |
| `detail` | `text` | Nullable additional context |
| `created_at` | `timestamptz` | NOT NULL |
| `auto_resolved` | `boolean` | NOT NULL, default false |
| `resolved_at` | `timestamptz` | Nullable |
| `resolved_by_id` | `uuid` | Nullable FK -> platform_users(id) |
| `resolved_reason` | `text` | Nullable; required for Critical severity |
| `acknowledged_at` | `timestamptz` | Nullable |
| `acknowledged_by_id` | `uuid` | Nullable FK -> platform_users(id) |
| `auto_dismissed` | `boolean` | NOT NULL, default false; Info alerts dismissed after 48h |

**Index:** `(severity, created_at DESC)`, `(tenant_id, severity)`, `(alert_code)`, partial index on unresolved alerts

### `platform_providers`

Seeded metadata-only catalog for every provider card shown in Developer Platform System Config.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `provider_key` | `varchar(50)` | UNIQUE NOT NULL |
| `display_name` | `varchar(100)` | NOT NULL |
| `provider_family` | `varchar(50)` | `oauth_app`, `transactional_email`, `infrastructure`, `object_storage`, `ai_verification`, `payment_gateway` |
| `is_active` | `boolean` | NOT NULL |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | NOT NULL |

**Seeded rows:** `google`/Google/`oauth_app`; `github`/GitHub/`oauth_app`; `microsoft`/Microsoft/`oauth_app`; `zoom`/Zoom/`oauth_app`; `sendgrid`/SendGrid/`transactional_email`; `resend`/Resend/`transactional_email`; `cloudflare`/Cloudflare/`infrastructure`; `cloudflare_r2`/Cloudflare R2/`object_storage`; `aws_rekognition`/AWS Rekognition/`ai_verification`; `stripe`/Stripe/`payment_gateway`; `payhere`/PayHere/`payment_gateway`; `paddle`/Paddle/`payment_gateway`.

**Rules:** Metadata only; no secret or credential payload is seeded or stored here. Provider rows are ONEVO-managed seed data, not arbitrary frontend free text. System Config reads active cards here and resolves configured state from family-specific credential tables.

### `platform_service_keys`

ONEVO-owned third-party service API keys used internally across all tenants. Examples: Resend/SendGrid for platform transactional email, Cloudflare DNS/WAF, Cloudflare R2. This is global platform credential storage, not tenant-level notification provider configuration.

Columns:
- `id` uuid PK
- `service_key` varchar(50) UNIQUE NOT NULL, FK -> platform_providers(provider_key)
  - examples: `resend`, `sendgrid`, `cloudflare`, `cloudflare_r2`
- `display_name` varchar(80) NOT NULL
- `api_key_encrypted` text NOT NULL
  - AES-256 encrypted
  - never returned by GET APIs
- `is_active` boolean NOT NULL
- `last_verified_at` timestamptz nullable
- `updated_by_id` uuid FK -> platform_users(id)
- `updated_at` timestamptz NOT NULL

Index:
- UNIQUE(`service_key`)

> **`platform_service_keys` and `notification_channels` are not interchangeable.**
>
> - `platform_service_keys` stores ONEVO-owned global/platform service credentials.
> - `notification_channels` stores tenant-level notification delivery channel configuration/routing.
> - In Phase 1, transactional/system email uses ONEVO-owned Resend/SendGrid credentials from `platform_service_keys`.
> - Provider family must be `transactional_email`, `infrastructure`, `object_storage`, or `ai_verification`.
> - `api_key_encrypted` may encrypt a single opaque key or one approved versioned composite JSON credential object (Cloudflare R2/AWS Rekognition) as a whole. Plaintext JSON/fields are never stored or returned.
> - OAuth app and payment gateway credentials are forbidden here.
> - Platform-owned SendGrid/Resend keys must not be stored in `notification_channels`.
> - Tenant-owned email provider credentials are not a Phase 1 requirement unless explicitly approved by a future product decision.
> - Slack notification delivery remains Phase 2.

### `platform_oauth_apps`

ONEVO's OAuth app registrations used when tenants or employees connect integrations via the ONEVO app consent flow. These are ONEVO developer app metadata, not tenant or user tokens. Client secrets are stored in `platform_oauth_app_credentials`.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `provider` | `varchar(30)` | UNIQUE FK -> platform_providers(provider_key); selected from active `oauth_app` provider cards |
| `app_name` | `varchar(100)` | NOT NULL; shown in OAuth consent screen |
| `logo_url` | `varchar(500)` | Nullable; uploaded via `POST /admin/v1/uploads/oauth-app-logo` |
| `client_id` | `varchar(200)` | NOT NULL; not encrypted, used in redirect URLs |
| `authorization_url` | `varchar(500)` | NOT NULL |
| `token_url` | `varchar(500)` | NOT NULL |
| `default_scopes` | `text[]` | NOT NULL |
| `is_active` | `boolean` | NOT NULL |
| `last_verified_at` | `timestamptz` | Nullable |
| `updated_by_id` | `uuid` | FK -> platform_users(id) |
| `updated_at` | `timestamptz` | NOT NULL |

**Index:** `UNIQUE(provider)`

**Provider rule:** Protocol endpoints/scopes remain backend-owned. `platform_oauth_apps` cannot be used for SendGrid, Resend, Cloudflare, Cloudflare R2, AWS Rekognition, Stripe, PayHere, or Paddle.

### `platform_oauth_app_credentials`

Encrypted credential versions for ONEVO's OAuth app registrations. Secret rotation creates a new credential row; previous rows are deactivated instead of overwritten. Plaintext secrets are never returned by API responses.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `platform_oauth_app_id` | `uuid` | FK -> platform_oauth_apps(id) |
| `client_secret_encrypted` | `text` | NOT NULL; AES-256 encrypted |
| `private_key_encrypted` | `text` | Nullable; GitHub App private key or provider-specific secret material |
| `encryption_key_version` | `varchar(50)` | NOT NULL |
| `credential_version` | `integer` | NOT NULL; monotonic per OAuth app |
| `is_active` | `boolean` | NOT NULL |
| `rotated_by_id` | `uuid` | FK -> platform_users(id) |
| `rotated_at` | `timestamptz` | NOT NULL |
| `deactivated_by_id` | `uuid` | Nullable FK -> platform_users(id) |
| `deactivated_at` | `timestamptz` | Nullable |

**Business rule:** one active credential row per `platform_oauth_app_id`. Tenant approval/configuration and true tenant-owned tokens are stored in `tenant_integration_credentials`; generic user-owned OAuth tokens are stored in `user_integration_connections`; Google/Outlook Calendar tokens and sync state remain in `external_calendar_connections`.

---

# Developer Platform Extensions (7 tables)

> Cross-module Developer Platform tables backing the Demo Profile / Demo Request
> approval flow and subscription plan modules/add-ons/pricing. Not enumerated as
> headings anywhere else in this document (a gap in this file, not a Phase 2
> deferral - they do not appear in the "Excluded as Phase 2" list either), but
> documented in `developer-platform/database/schema.md` and required by
> `backend/CLAUDE.md`'s "ONEVO-HR coverage rule". Added here after a schema
> audit confirmed real, already-built functionality depends on them. See
> `PHASE1_CANONICAL_TABLE_AUDIT.md` "Documented Extras" section for the
> per-table redesign rationale.

### `demo_profiles`

Controls demo/trial tenant behavior and the upgrade choices visible to demo customers.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `name` | `varchar(120)` | NOT NULL, UNIQUE |
| `description` | `varchar(500)` | Nullable |
| `trial_duration_days` | `int` | NOT NULL |
| `auto_expire` | `boolean` | NOT NULL, default true |
| `max_employees` | `int` | NOT NULL |
| `demo_storage_limit_gb` | `int` | NOT NULL |
| `demo_ai_token_limit` | `bigint` | NOT NULL |
| `is_active` | `boolean` | NOT NULL, default true |
| `created_by_platform_user_id` | `uuid` | Nullable FK -> platform_users |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | Nullable |

### `demo_profile_modules`

Per-module access level and feature entitlements granted to a demo profile.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `demo_profile_id` | `uuid` | FK -> demo_profiles, NOT NULL |
| `module_catalog_id` | `uuid` | FK -> module_catalog, NOT NULL |
| `access_level` | `varchar(20)` | `full_access`, `view_only`, or `archive` |
| `feature_permissions` | `jsonb` | Map of `feature_key -> enabled` for features inside this module |

**Unique:** `(demo_profile_id, module_catalog_id)`

### `demo_profile_upgrade_options`

One row per demo profile controlling which paid plans and add-ons a demo tenant may upgrade to.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `demo_profile_id` | `uuid` | FK -> demo_profiles, NOT NULL, UNIQUE (1:1) |
| `allowed_plan_ids` | `jsonb` | Array of allowed `subscription_plans.id` values |
| `allowed_addon_module_keys` | `jsonb` | Array of allowed add-on module keys |
| `hidden_addon_module_keys` | `jsonb` | Array of hidden add-on module keys |
| `addon_visibility` | `jsonb` | Map of `module_key -> "enabled" \| "show_only"` |
| `addon_demo_limits` | `jsonb` | Map of demo-specific limits per add-on |

### `demo_access_requests`

Public/demo inquiry requests requiring platform-side approval before a demo tenant is created or updated. Request intake only.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `company_name` | `varchar(200)` | NOT NULL |
| `requester_name` | `varchar(160)` | NOT NULL |
| `requester_email` | `varchar(255)` | NOT NULL |
| `requester_phone` | `varchar(50)` | Nullable |
| `requested_subdomain` | `varchar(120)` | Nullable |
| `company_website` | `varchar(255)` | Nullable |
| `country_code` | `varchar(3)` | |
| `requested_company_size_range` | `varchar(30)` | |
| `requested_demo_profile_id` | `uuid` | Nullable FK -> demo_profiles |
| `requested_module_keys` | `jsonb` | Array of requested module keys |
| `requested_access_notes` | `text` | Nullable |
| `status` | `varchar(30)` | `submitted`, `approved`, `rejected`, `converted_to_demo` |
| `source` | `varchar(40)` | e.g. `landing_demo_form` |
| `created_at` | `timestamptz` | |
| `reviewed_at` | `timestamptz` | Nullable |
| `reviewed_by_id` | `uuid` | Nullable FK -> platform_users |
| `rejection_reason` | `varchar(500)` | Nullable |
| `admin_notes` | `text` | Nullable; internal review notes |
| `tenant_visible_note` | `text` | Nullable; sent to applicant on approve/reject |
| `created_tenant_id` | `uuid` | Nullable FK -> tenants; set after approval creates the demo tenant |
| `metadata` | `jsonb` | Nullable; campaign/source context |

### `subscription_plan_modules`

Explicit package classification for a plan's selected modules - source of truth for base vs optional-addon.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `subscription_plan_id` | `uuid` | FK -> subscription_plans, NOT NULL |
| `module_key` | `varchar(80)` | FK/logical reference -> module_catalog.module_key |
| `package_type` | `varchar(20)` | `base` or `optional_addon` |
| `storage_contribution_gb` | `int` | Nullable |
| `ai_token_contribution` | `bigint` | Nullable |
| `is_active` | `boolean` | NOT NULL, default true |
| `created_at` | `timestamptz` | |

**Unique:** `(subscription_plan_id, module_key)`

### `subscription_plan_resource_addons`

Resource-only add-ons for a specific plan (not modules) - increase the tenant's shared storage pool and/or AI token allowance.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `subscription_plan_id` | `uuid` | FK -> subscription_plans, NOT NULL |
| `label` | `varchar(120)` | e.g. `Extra Storage Pack` |
| `storage_contribution_gb` | `int` | Nullable |
| `ai_token_contribution` | `bigint` | Nullable |
| `price_by_employee_tier` | `jsonb` | Map of `employee_count_tier -> unit_price` |
| `is_active` | `boolean` | NOT NULL, default true |
| `created_at` | `timestamptz` | |

### `subscription_plan_price_brackets`

Company-size pricing tiers per plan.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `subscription_plan_id` | `uuid` | FK -> subscription_plans, NOT NULL |
| `company_size_range` | `varchar(20)` | e.g. `51-200` |
| `base_plan_monthly_price` | `numeric(12,2)` | NOT NULL |
| `annual_price` | `numeric(12,2)` | NOT NULL; immutable annual price for this plan/company-size bracket |
| `optional_addon_prices` | `jsonb` | Map of `module_key -> monthly_price` |
| `resource_addon_prices` | `jsonb` | Map of `addon_id -> unit_price` |
| `currency` | `varchar(3)` | ISO 4217 |
| `created_at` | `timestamptz` | |

**Unique:** `(subscription_plan_id, company_size_range)`

---

## Known documentation discrepancies

- Phase 1 now includes database-backed global lookup/reference tables for approval statuses, employment statuses, employment types, severities, and work modes.
- Tenant/admin web auth uses cookie-backed server sessions while agent/device auth may use device-scoped JWTs.
- gdpr_consent_records is legacy/non-canonical naming for legal_acceptance_records.
- idempotency_records is canonical for generic command/request idempotency.
- **`monitoring_alert_policy`** is fully documented in `configuration.md` and included in `database/schema-catalog.md`.
- **Exception Engine** is Phase 2 (consistent with CLAUDE.md). Excluded here.
- **Developer Platform**: release/ring management tables (`agent_version_releases`, `agent_deployment_rings`, `agent_deployment_ring_assignments`) and `platform_api_keys` are Phase 2. Phase 1 Developer Platform inventory includes the platform user/session/RBAC/auth-event tables, `platform_alerts`, the metadata-only `platform_providers` catalog, family-owned `platform_service_keys`, and platform OAuth app registration/credential storage.
- **Legal pending-login durability**: `legal_login_challenges` is the Auth-owned Phase 1 persistence boundary for hashed pending-legal handles, CSRF binding, expiry, rotation, and final consumption. It is not a normal session, MFA challenge, or generic outbox row.
- **`refresh_tokens`** is canonically defined and counted once under Auth & Security. Shared Platform references that ownership and does not define a second schema. Tenant and platform-admin browser sessions do not create, rotate, or return refresh tokens.
- `database/schema-catalog.md` now separates Phase 1 rows from retained Phase 2 references; this inventory remains the table-creation gate for Phase 1.
- **`overtime_records`** is marked Phase 1 in `schemas/time-attendance.md`'s file-level `**Phase:**` header, but the Overtime feature (table, Overtime Rules screen, request/approval flow, `OvertimeRequested`/`OvertimeApproved` events) is demoted to Phase 2 by current product decision. `time-attendance.md` remains a mostly-Phase-1 file (17 of its 18 tables); only `overtime_records` is excluded here.
- **`platform_service_keys`** was defined in `developer-platform/database/schema.md` and the System Config module docs (ONEVO-owned Resend/Cloudflare/Cloudflare R2 keys; system email falls back to the Resend service key) but was missing from this inventory, blocking Phase 1 backend implementation of platform-owned service key storage. Added to the Developer Platform section on 2026-07-10. The related AI tables `ai_provider_configs` and `tenant_ai_provider_overrides` were deliberately NOT added: by product decision AI is used only for Agentic Chat, and Agentic Chat is Phase 2.
- **External integration OAuth credential storage**: `platform_oauth_apps`, `platform_oauth_app_credentials`, `integration_catalog`, `module_integration_links`, and `tenant_integration_credentials` were defined in `developer-platform/database/schema.md` and referenced by System Config / Module Catalog Manager docs but were missing from this Phase 1 inventory. Added on 2026-07-10 because Phase 1 System Config owns ONEVO OAuth app registrations for GitHub, Google, Microsoft, and Zoom, and the tenant app needs catalog/link/token boundaries documented before backend implementation. On 2026-07-13, `user_integration_connections` was added for generic user-owned OAuth connections after confirming that the only apparent equivalent (`external_account_connections` plus `microsoft_graph_tokens`) is Microsoft-specific and deferred to Phase 2. `external_calendar_connections` remains calendar-specific. `platform_service_keys`, `payment_gateway_credentials`, `notification_channels`, and `tenant_integration_credentials` must not be reused for a user's personal OAuth token.
- **Developer Platform dashboard/security/billing support tables**: `platform_alerts`, `webhook_event_queue`, and `billing_audit_logs` were defined in `developer-platform/database/schema.md` and referenced by Developer Platform dashboard, security-center, and payment webhook docs, but were missing from this Phase 1 inventory. Added on 2026-07-10 to resolve the strict inventory gate conflict. `platform_permission_catalog` was not added because it appeared only in a stale schema summary row and has no table definition.
- **Developer Platform Extensions (7 tables)** - `demo_profiles`, `demo_profile_modules`, `demo_profile_upgrade_options`, `demo_access_requests`, `subscription_plan_modules`, `subscription_plan_resource_addons`, `subscription_plan_price_brackets` - were entirely missing from this file's original heading list, despite backing real, already-built Demo Profile/Request approval and subscription functionality documented in `developer-platform/database/schema.md` and required by `backend/CLAUDE.md`'s "ONEVO-HR coverage rule". Discovered via a schema-vs-EF-model audit on 2026-07-06; the EF model previously used non-canonical shapes for this functionality (join tables joining permissions to individual features/plan-features where the canonical design only ever joins at the module level, e.g. `module_permission_ownership` and `role_templates.permission_codes_json`; and a global `SubscriptionAddOn` catalog where the canonical design is per-plan). The EF model was reshaped to match `schema.md` and the tables were added here rather than treated as Phase 2 (they are not listed under "Excluded as Phase 2" either - this was a gap, not a deferral).
