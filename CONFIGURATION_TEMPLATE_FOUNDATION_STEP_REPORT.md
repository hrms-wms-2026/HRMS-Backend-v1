# Configuration Template Manager Foundation — Step Report

## Docs read

The plan (`docs/superpowers/plans/2026-07-23-configuration-template-manager-foundation.md`)
was authored against 9 canonical `OneVo-HR` doc files plus `backend/api-contracts.md`.
This execution followed that plan directly rather than re-reading the source docs. One
inconsistency the plan carried forward and this step preserved: `database/schemas/shared-platform.md`'s
`configuration_templates.template_type` column note lists `org_structure`, while
`overview.md`, `end-to-end-logic.md`, `testing.md`, and `backend/api-contracts.md` all use
`position_template`. This implementation uses `position_template` (the value used
everywhere else, including the documented API filter).

## Schema comparison

`configuration_templates`: `id, template_key, template_type, name, description, version,
module_keys_json (jsonb), industry_profile_tag, payload_json (jsonb), is_system, is_active,
created_by_id (FK → platform_users), created_at, updated_at`. Unique index on
`template_key`, index on `template_type`. No `tenant_id` column, per the global constraint.

`tenant_configuration_template_applications`: `id, tenant_id (FK → tenants),
configuration_template_id (FK → configuration_templates), template_type, applied_version,
applied_payload_json (jsonb), custom_payload_json (jsonb, nullable), warnings_json (jsonb,
nullable), status (check constraint: only 'applied'), applied_by_id (FK → platform_users),
applied_at`. Indexes on `(tenant_id, applied_at)` and `configuration_template_id`.

Exactly these two tables exist — confirmed by the architecture test
`Migration_CreatesOnlyTheTwoCanonicalConfigurationTemplateTables`.

## Files changed

**Domain**
- `src/ONEVO.Domain/Features/DevPlatform/ConfigurationTemplates/Entities/ConfigurationTemplate.cs`
- `src/ONEVO.Domain/Features/DevPlatform/ConfigurationTemplates/Entities/TenantConfigurationTemplateApplication.cs`

**Infrastructure**
- `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/ConfigurationTemplates/ConfigurationTemplateConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/ConfigurationTemplates/TenantConfigurationTemplateApplicationConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (modified — 2 DbSets + using)
- `src/ONEVO.Infrastructure/Migrations/20260723164658_AddConfigurationTemplates.cs` (+ `.Designer.cs`)
- `src/ONEVO.Infrastructure/Migrations/20260724010252_AddConfigurationTemplateApplicationsRlsPolicy.cs` (+ `.Designer.cs`) — **not in the original plan**, added to fix a real architecture-test regression (see "Deviations from the plan" below)
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (modified)
- `src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/ConfigurationTemplates/EfConfigurationTemplateRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/ConfigurationTemplates/EfTenantConfigurationTemplateApplicationRepository.cs`
- `src/ONEVO.Infrastructure/DependencyInjection.cs` (modified — 2 registrations + 2 usings)

**Application**
- `Features/DevPlatform/ConfigurationTemplates/RepositoryInterfaces/IConfigurationTemplateRepository.cs`
- `Features/DevPlatform/ConfigurationTemplates/RepositoryInterfaces/ITenantConfigurationTemplateApplicationRepository.cs`
- `Features/DevPlatform/ConfigurationTemplates/Helpers/ConfigurationTemplateModuleRequirement.cs`
- `Features/DevPlatform/ConfigurationTemplates/DTOs/Requests/ConfigurationTemplateRequests.cs`
- `Features/DevPlatform/ConfigurationTemplates/DTOs/Responses/ConfigurationTemplateResponses.cs`
- `Features/DevPlatform/ConfigurationTemplates/DTOs/Responses/TenantConfigurationTemplateApplicationResponses.cs`
- `Features/DevPlatform/ConfigurationTemplates/Mappers/ConfigurationTemplateMapper.cs`
- `Features/DevPlatform/ConfigurationTemplates/Queries/ListConfigurationTemplates/*` (Query + Handler)
- `Features/DevPlatform/ConfigurationTemplates/Queries/GetConfigurationTemplateDetail/*` (Query + Handler)
- `Features/DevPlatform/ConfigurationTemplates/Queries/ListTenantConfigurationTemplateApplications/*` (Query + Handler)
- `Features/DevPlatform/ConfigurationTemplates/Commands/CreateConfigurationTemplate/*` (Command + Handler)
- `Features/DevPlatform/ConfigurationTemplates/Commands/UpdateConfigurationTemplateMetadata/*` (Command + Handler)
- `Features/DevPlatform/ConfigurationTemplates/Commands/DeactivateConfigurationTemplate/*` (Command + Handler)
- `Features/DevPlatform/ConfigurationTemplates/Commands/CloneConfigurationTemplate/*` (Command + Handler)
- `Features/DevPlatform/ConfigurationTemplates/Commands/ApplyConfigurationTemplateToTenant/*` (Command + Handler)

**Api**
- `Controllers/Admin/DevPlatform/ConfigurationTemplates/AdminConfigurationTemplatesController.cs`
- `Controllers/Admin/DevPlatform/Tenants/AdminTenantConfigurationTemplatesController.cs`

**Tests**
- `tests/ONEVO.Tests.Unit/Features/DevPlatform/ConfigurationTemplates/*` — 8 handler test classes
- `tests/ONEVO.Tests.Architecture/ConfigurationTemplateManagerArchitectureTests.cs`
- `tests/ONEVO.Tests.Integration/DevPlatform/ConfigurationTemplateManagerIntegrationTests.cs` (written, **not executed** — see gaps)

## Migration name and exact tables created

`20260723164658_AddConfigurationTemplates` — creates exactly `configuration_templates` and
`tenant_configuration_template_applications`, nothing else (verified by architecture test).

A second migration, `20260724010252_AddConfigurationTemplateApplicationsRlsPolicy`, adds
row-level-security to `tenant_configuration_template_applications` only (raw SQL, no new
tables/columns).

## APIs added

| Method | Route | Permission |
|---|---|---|
| GET | `/admin/v1/configuration-templates` | `platform.templates.read` |
| GET | `/admin/v1/configuration-templates/{templateId}` | `platform.templates.read` |
| POST | `/admin/v1/configuration-templates` | `platform.templates.manage` |
| PATCH | `/admin/v1/configuration-templates/{templateId}` | `platform.templates.manage` |
| DELETE | `/admin/v1/configuration-templates/{templateId}` | `platform.templates.manage` |
| POST | `/admin/v1/configuration-templates/{templateId}/clone` | `platform.templates.manage` |
| POST | `/admin/v1/tenants/{tenantId}/configuration-templates/{templateId}/apply` | `platform.templates.manage` |
| GET | `/admin/v1/tenants/{tenantId}/configuration-template-applications` | `platform.tenants.read` |

## Permissions used

`platform.templates.read`, `platform.templates.manage`, `platform.tenants.read` — all
pre-existing (`PlatformPermissionCatalog`), none added.

## Tests added/results

**Unit** (`tests/ONEVO.Tests.Unit/Features/DevPlatform/ConfigurationTemplates/`), all passing:
- `ListConfigurationTemplatesQueryHandlerTests` — 2/2
- `GetConfigurationTemplateDetailQueryHandlerTests` — 2/2
- `CreateConfigurationTemplateCommandHandlerTests` — 5/5 (includes a security regression test added mid-implementation, see below)
- `UpdateConfigurationTemplateMetadataCommandHandlerTests` — 3/3
- `DeactivateConfigurationTemplateCommandHandlerTests` — 2/2
- `CloneConfigurationTemplateCommandHandlerTests` — 2/2
- `ApplyConfigurationTemplateToTenantCommandHandlerTests` — 7/7
- `ListTenantConfigurationTemplateApplicationsQueryHandlerTests` — 2/2

Total new: 25/25 passing. Full filtered run (`ConfigurationTemplate|TemplateApplication|CreateTenant|Provisioning`): **46/46 passing**.

**Architecture**: 7 new facts in `ConfigurationTemplateManagerArchitectureTests`, all passing.
Full suite: **156/156 passing** (includes pre-existing `SetupOptionModelRetirementArchitectureTests`,
`TenantOneTimeChargeActiveRetirementArchitectureTests`, and `LayerDependencyTests`).

**Integration**: written but **not executed** — Docker Desktop's engine was not reachable in
this environment (`docker info` succeeds for the CLI/client section but fails to reach the
`dockerDesktopLinuxEngine` named pipe; the daemon itself is not running). Per the plan's Task 18
Step 2 instruction, execution was skipped rather than forced. The task's checkbox is left
unchecked pending a Docker-enabled run of:
`dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "ConfigurationTemplateManagerIntegrationTests"`

## Explicit confirmation

No `setup_services`, `tenant_setup_services`, `tenant_setup_selections`, or
`tenant_one_time_charges` tables, repositories, or handlers were added or reactivated by this
work. A repo-wide search (`rg` over `src` and `tests`) confirms every remaining hit for these
terms falls into one of: historical migration/snapshot residue (`Migrations/*.cs`,
`*.Designer.cs`, `ApplicationDbContextModelSnapshot.cs`), report-only documents
(`*_RETIREMENT_REPORT.md`, `SETUP_SERVICES_TEMPLATE_MODEL_RECONCILIATION_REPORT.md`),
architecture-guard tests (`SetupOptionModelRetirementArchitectureTests.cs`,
`TenantOneTimeChargeActiveRetirementArchitectureTests.cs`,
`ConfigurationTemplateManagerArchitectureTests.cs`'s own banned-term list, and
`CreateTenantConfigurationSetupTests.cs` which proves the old contract is retired), or the
pre-existing inactive-legacy EF configurations (`TenantSetupSelectionConfiguration.cs`,
`TenantOneTimeChargeConfiguration.cs`) that map the still-present-but-unused legacy tables —
none of these reactivate a repository, handler, or route. `tenant_setup_selections` and
`tenant_one_time_charges` remain present only as pre-existing inactive legacy, unchanged by
this work.

## Deviations from the plan

1. **Added RLS policy migration (not in the original plan).** Running the full architecture
   suite after Task 17 (as its Step 3 instructs) surfaced a real regression:
   `TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` failed
   because `tenant_configuration_template_applications` implements `ITenantOwnedEntity` but had
   no `tenant_isolation` RLS policy. Fixed with a second migration
   (`AddConfigurationTemplateApplicationsRlsPolicy`) following the exact admin-bypass policy
   pattern used by the pre-existing `AddMissingRlsPolicies` migration. Full architecture suite
   is green (156/156) after this fix.
2. **Security fix to `CreateConfigurationTemplateCommandHandler` (not in the original plan).**
   An automated security review of the commit flagged that the handler trusted
   `request.IsSystem` from the client, letting any admin with `platform.templates.manage`
   mark a template as `IsSystem = true` (which makes it immutable via the update endpoint —
   normally reserved for platform-curated/seeded templates). Nothing in this foundation step
   seeds system templates through the API, so the handler now always creates
   `IsSystem = false` regardless of client input. A regression test
   (`Handle_RequestingIsSystemTrue_IsIgnored_CreatedTemplateIsNeverSystem`) was added.
3. **Integration tests not executed** — see above; Docker daemon unavailable in this
   environment, not a code issue.

## Remaining gaps (carried forward per plan, not attempted here)

- Downstream module payload execution (`tenant_settings`, `positions`, `time_off_types`,
  `monitoring_feature_toggles`, `app_allowlists`, `checklist_templates`,
  `data_import_mapping_templates`) is deferred; applying a template writes only the immutable
  audit row plus a `warnings_json` entry stating this.
- Per-type `payload_json` schema validation is limited to "must be a valid JSON object" —
  full per-type field validation is deferred.
- `DELETE .../configuration-templates/{id}` deactivation is unconditional — the documented
  "blocked if active tenant positions/assignment rows reference the template" guard needs the
  downstream module tables this step does not build.
- `GET .../configuration-templates/{id}` "version history" is the current `version` int plus
  this template's own `tenant_configuration_template_applications` rows — no separate
  version-history table exists in the Phase 1 inventory for this feature.
- No "reactivate" endpoint (not in the documented API catalog).
- Template recommendation is limited to the documented `type`/`active_only`/`industry_tag`
  filters — no company-size/country-ranking algorithm was built, since `backend/api-contracts.md`
  does not define that contract for this feature.
