# Position Template Packs — Backend Foundation

Read/seed-only foundation so tenant HR users can eventually load seeded Position Template Packs
from the Position screen. No frontend drag/drop, no apply-to-tenant logic — that is Part 2.

## 1. Files changed

**New:**
- `src/ONEVO.Application/Features/OrgStructure/PositionTemplatePacks/DTOs/PositionTemplatePackPayload.cs` — internal `payload_json` deserialization records (snake_case via `JsonPropertyName`, matching the documented `position_template` schema exactly).
- `src/ONEVO.Application/Features/OrgStructure/PositionTemplatePacks/DTOs/PositionTemplatePackResponses.cs` — public response DTOs (`PositionTemplatePackDto`, `PositionTemplatePackPositionDto`, `PositionTemplatePackListResponseDto`).
- `src/ONEVO.Application/Features/OrgStructure/PositionTemplatePacks/Mappers/PositionTemplatePackMapper.cs` — parses + validates a `ConfigurationTemplate` row into the response shape; returns `false` (no throw) on any malformed/incomplete payload.
- `src/ONEVO.Application/Features/OrgStructure/PositionTemplatePacks/Queries/ListPositionTemplatePacks/ListPositionTemplatePacksQuery.cs` and `...QueryHandler.cs` — the read use case.
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionTemplatePacksController.cs` — tenant-facing controller.
- `src/ONEVO.Infrastructure/Persistence/Seeders/PositionTemplatePackSeeder.cs` — idempotent boot-time seeder for the 7 system packs.
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/PositionTemplatePacks/ListPositionTemplatePacksQueryHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/PositionTemplatePacks/EfConfigurationTemplateRepositoryPositionTemplateFilterTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/PositionTemplatePacks/PositionTemplatePackSeederTests.cs`
- `tests/ONEVO.Tests.Architecture/PositionTemplatePacksControllerArchitectureTests.cs`

**Modified:**
- `src/ONEVO.Infrastructure/DependencyInjection.cs` — registered `PositionTemplatePackSeeder` as a hosted service, immediately after `PlatformAccessSeeder` (dependency: needs at least one `platform_users` row) and before `ModuleCatalogSeeder`.
- `src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/ConfigurationTemplates/EfConfigurationTemplateRepository.cs` — added `.AsNoTracking()` to the shared `ListAsync`/`CountAsync` query (`BuildFilteredQuery`). `GetByIdAsync` was deliberately **not** changed — it's used by `UpdateConfigurationTemplateMetadataCommandHandler` and other admin commands that mutate the tracked entity before `SaveChangesAsync`.

**No new table, no new migration.** Confirmed via the doc read and the existing `configuration_templates` schema that `template_type = 'position_template'` already supports everything this task needs.

## 2. Endpoint contract

```
GET /api/v1/org/position-template-packs
```
- `[Authorize(Policy = "TenantPolicy")]` + `[RequirePermission("org:read")]` on the action.
- No `tenantId` accepted anywhere (route, query, body). Tenant comes from `ITenantContext.TenantId` inside the handler only (same pattern as the sibling `GetPositionAccessQueryHandler`/`SetPositionAccessCommandHandler` in the same `PositionsController` family — verified `HostTenantResolutionMiddleware` populates `ITenantContext` on every tenant request, not conditionally).
- Response: `{ "items": [ { id, templateKey, name, description, industryProfileTag, employeeCountRangeKey, employeeCountMin, employeeCountMax, positions: [ { positionKey, positionName, departmentName, reportsToPositionKey, linkedRoleTemplateId } ] } ] }` — camelCase via ASP.NET Core's default `System.Text.Json` serialization (`AddControllers()` has no custom naming policy override, confirmed in `Program.cs`).
- Only `template_type = position_template` and `is_active = true` rows are ever returned; further filtered by module entitlement (see §4).
- No pagination fields are exposed — the underlying read is internally bounded to 200 rows (`MaxTemplates`) as a safety cap, not exposed as page/pageSize. Phase 1 seeds 7 rows.

## 3. Seed data added

`PositionTemplatePackSeeder` (idempotent by `template_key`, checked individually so partial catalogs self-heal on restart):

| `template_key` | Name | Range | Positions |
|---|---|---|---|
| `executive-leadership-template` | Executive Leadership Template | 101-500 | CEO, COO, CTO, CFO, CHRO/Head of People, Department Director |
| `hr-people-operations-template` | HR / People Operations Template | 51-100 | HR Manager, HR Business Partner, Talent Acquisition Specialist, Payroll Specialist, People Ops Coordinator, L&D Coordinator |
| `management-layer-template` | Management Layer Template | 101-500 | General Manager, Department Manager, Operations Manager, Assistant Manager, Supervisor |
| `team-lead-template` | Team Lead Template | 11-50 | Team Lead, Technical Lead, Shift Lead, Senior Software Engineer |
| `project-delivery-management-template` | Project / Delivery Management Template | 51-100 | Delivery Manager, Program Manager, Project Manager, Scrum Master, Product Owner, Project Coordinator |
| `software-engineering-starter-template` | Software / Engineering Starter Template | 11-50 | Engineering Manager, Software Engineer, QA Engineer, DevOps Engineer, UI/UX Designer |
| `operations-starter-template` | Operations Starter Template | 11-50 | Operations Manager, Operations Executive, Admin Officer, Office Coordinator |

Every row: `template_type = position_template`, `is_system = true`, `is_active = true`, `module_keys_json = ["core_hr"]` (matches the doc's Module Entitlement Guard table). `industry_profile_tag` (the entity column) is left `null` for all seven — the docs reserve that column for `monitoring_policy` templates; the payload's own `industry` field is used instead (see §5 for how that reaches the API response).

`linked_role_template_id` is `null` on every position in every pack. The only global role templates that currently exist (`RoleTemplateSeeder`: "HR Manager", "Workspace Member") don't map safely to most of these concrete positions (CEO, COO, Team Lead, Project Manager, etc.), so nothing is guessed. Wiring real linkage is future work once a broader role-template catalog exists.

## 4. Permissions and entitlement

- Endpoint permission: `org:read` (existing tenant permission pattern, same as `PositionsController.List`).
- Module entitlement filtering: for each `position_template` row, every module key in that row's `module_keys_json` (currently just `core_hr`) is checked via the existing `IModuleEntitlementService.IsModuleEnabledAsync(tenantId, moduleKey)`. A tenant not entitled to a required module simply doesn't see that pack in `items` — no error. This reuses the same entitlement service the Apply flow uses (`ModuleEntitlementService`, backed by `tenant_subscriptions.selected_modules_json`); no new entitlement mechanism was invented.

## 5. Unresolved backend/frontend contract risk — flag for Part 2

`industryProfileTag` in the response is sourced from `payload_json.industry`, **not** the `configuration_templates.industry_profile_tag` column — the docs state that column applies to `monitoring_policy` templates only, so seeding it for `position_template` rows would contradict the schema notes. The response field name still matches the contract given for this task (`"industryProfileTag": "software"` for the example pack), so this is a deliberate mapping decision, not an oversight. **Confirm this is the intended source before Part 2 builds any industry-based filtering/auto-selection UI against it.**

## 6. Tests run

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj                      → 0 errors
dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj       → 0 errors
dotnet build tests/ONEVO.Tests.Architecture/...csproj             → 0 errors, 0 warnings
dotnet test  --filter FullyQualifiedName~PositionTemplatePacks    → 12/12 passed (Unit)
dotnet test  --filter FullyQualifiedName~PositionTemplatePacksController → 9/9 passed (Architecture)
dotnet test  --filter FullyQualifiedName~ConfigurationTemplates   → 25/25 passed (Unit; regression check
                                                                     after the AsNoTracking repository change)
git diff --check                                                  → clean
```

Coverage against the task's required test list:
- Seeder idempotency — `SeedAsync_IsIdempotent_WhenRunTwice` (EF InMemory, run twice, row count unchanged).
- `template_type = position_template` — `SeedAsync_WithPlatformUser_SeedsSevenActiveSystemPositionTemplates`.
- Returns only active `position_template` — `Handle_RequestsOnlyActivePositionTemplateType_FromRepository` (handler contract) + `ListAsync_PositionTemplateActiveOnly_ExcludesOtherTypesAndInactiveRows` (real EF query against seeded `configuration`/`time_off_policy`/`onboarding`/inactive rows, proving the shared repository filter actually excludes them, not just a mock expectation).
- Does not accept `tenantId` — `NoAction_AcceptsTenantIdParameter` + `Query_HasNoTenantIdProperty` (architecture).
- Gated by `org:read` — `ListAction_IsHttpGet_AndRequiresOrgReadPermission` (architecture).
- Payload mapping → documented shape — `Handle_MapsPayload_ToDocumentedResponseShape`.
- Malformed payload handled safely — `Handle_InvalidJsonPayload_ReturnsSafeServerError_WithoutLeakingParseException` (non-JSON string) and `Handle_PayloadMissingRequiredPositions_ReturnsSafeServerError` (valid JSON, missing required field) — two distinct failure paths, both asserted to return `500` without leaking `JsonException`/line/path details in `result.Error`.
- Repository reads use `AsNoTracking` — verified by code change + full ConfigurationTemplates regression pass (no admin update flow broken).

## 7. Skipped checks and why

- **Full integration suite** (Testcontainers/Postgres) — not run, per instructions ("do not run unless needed"). This foundation adds no migration and reuses an already-integration-tested table/repository (`ConfigurationTemplateManagerIntegrationTests` already covers `configuration_templates` FK/RLS behavior); the new EF InMemory tests substitute for that here.
- **Frontend** — untouched, as instructed.
- **Commit/push** — not performed, as instructed.

## 8. Other unresolved risks

- `PositionTemplatePackSeeder` silently skips (logs an info message, no exception) if `platform_users` is empty when it runs — `configuration_templates.created_by_id` is `NOT NULL` with a `Restrict` FK, so there is no safe placeholder value. In Development/Test this is a non-issue because `PlatformAccessSeeder` (registered immediately before it) bootstraps a Super Admin whenever `PlatformBootstrap:SuperAdminEmail` is configured. In an environment where that config is absent, the packs simply won't appear until a platform user exists and the service restarts — this is a real deployment-order dependency, not just a footnote.
- **Inconsistent malformed-data handling by design, not oversight:** a `position_template` row whose `module_keys_json` fails to parse is treated as "no required modules" (the pack stays visible to every tenant), while a row whose `payload_json` fails to parse or validate fails the *entire* request with a safe `500`. This asymmetry is intentional for a read-only surface — worst case for the module-keys path is an unentitled tenant merely *seeing* a pack, not applying one. **This fail-open behavior must not be inherited by the future apply-to-tenant endpoint**, where an unentitled apply would be a real entitlement bypass, not just a visibility leak.
- The tenant-facing query handler bounds its `configuration_templates` read to 200 rows (`MaxTemplates`, not exposed as pagination) as a defensive cap; the seeder test now asserts the seeded catalog (7 rows) stays well under that bound so growth past it is a conscious decision rather than a silent truncation.
