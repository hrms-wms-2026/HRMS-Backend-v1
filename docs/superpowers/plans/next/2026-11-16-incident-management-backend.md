# Incident Management — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a platform-side Incident module to the backend: incidents with severity, lifecycle, timeline, affected tenants, team alerts, targeted customer updates (banner + email), escalation into Work Management bugs in an internal tenant with idempotent retry, status sync back, and a small postmortem.

**Architecture:** New `DevPlatform/Incidents` feature (Domain entities, Application commands/queries on MediatR, EF configurations and one migration per task area, `AdminIncidentsController`). The only cross-tenant write is a single guarded writer that switches into the flagged, configured internal tenant inside one transaction and back to admin mode, and creates tasks through `ITaskWriteService`. Customer updates reuse platform announcements with a new `selected_tenants` audience and email through the outbox. Work Management status changes on incident-linked tasks raise an outbox message that updates the incident.

**Tech Stack:** .NET 10, ASP.NET Core, MediatR, EF Core on SQL Server with RLS (`SESSION_CONTEXT`), xUnit + Moq + FluentAssertions, SQL Server integration tests.

**Spec:** `docs/superpowers/specs/next/2026-11-16-incident-management-design.md` (branch `spec/incident-management`, approved at `943fec23`, DEC-030).

**Scope:** spec Phase 1 only. Phase 2 (Azure Monitor alert intake) gets its own plan after the observability foundation (task-17) exists.

**Repos:** backend `HRMS-Backend-v1` only. Admin UI and tenant UI have their own plans (`2026-11-16-incident-management-admin-ui.md`, `2026-11-16-incident-management-tenant-ui.md`), which consume the API defined here.

## Global Constraints

- **Starts after** backend #227 (`feat/platform-support-module`) and F-07 (`feat/sec-f07-platform-audit-log`) are merged into `development`. Branch from that `development` as `feat/incident-management-backend`.
- Severities exactly `critical`, `major`, `minor`. Statuses exactly `investigating`, `identified`, `monitoring`, `resolved`, `closed`.
- Work link kinds exactly `engineering_bug`, `postmortem_action`; link statuses exactly `pending`, `created`, `failed`.
- New announcement audience exactly `selected_tenants`. Severity mapping to announcements: critical → `critical`, major → `warning`, minor → `info`.
- Permissions exactly `platform.incidents.read`, `platform.incidents.manage`, `platform.incidents.configure`. Super Admin gets all three through the existing catalog seeding; no other role gets `configure`.
- Every write command implements `IAuditedCommand<TResponse>`. `DescribeAudit` never includes update bodies, postmortem text, descriptions or customer contact emails: ids, numbers, statuses, severities and counts only.
- Nothing is sent to customers without the explicit Send command. Changing status never sends.
- Customer tenant names never go into internal-tenant tasks: titles and descriptions get the incident number, severity, status and the count of affected tenants only.
- The only cross-tenant write path is `IInternalTenantWorkWriter`. It refuses any tenant that is not both `is_internal = 1` and the configured internal tenant.
- Readable incident numbers are formatted `INC-{n}` from an incrementing integer.
- Every new table with a `tenant_id` gets `RlsPredicate.Tenant`, a `RlsSql.CreatePolicy` call in its own migration, an entry in `RlsTableRegistry.AddedAfterBaseline`, and its name in `SchemaParityTests.TablesAddedAfterBaseline`. Tables without `tenant_id` are only added to the parity list.
- Migrations: `dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`. Never hand-edit designer files.
- API routes under `admin/v1/incidents`, snake_case query parameters, `[Authorize(Policy = "AdminPolicy")]` + `[RequirePlatformPermission(...)]`, actor from `ICurrentPlatformUserContext.UserId`, same `ToResponse` pattern as `AdminAnnouncementsController`.

## Review Focus

- **Double-pressed Send** (two Send requests for the same status within seconds): exactly one banner change, one email batch and one timeline event. Test in Task 7.
- **Incident with no affected tenants:** Send returns 400 "Add at least one affected tenant before sending"; escalation still works. Test in Task 7.
- **Critical incident when no platform user holds `platform.incidents.read`:** the team alert falls back to `Support:AlertEmail`; if that is empty too, it logs a warning and the declare still succeeds. Test in Task 5.
- **Internal tenant configuration that has gone stale** (project or objective archived, category deleted): escalation returns 409 with "Internal tenant configuration is no longer valid" and writes no link row. Test in Task 10.
- **An escalated bug is deleted in Work Management:** its work link stops counting toward "Fix ready" (treated as not done), the incident timeline records it. Test in Task 11.

---

### Task 1: Tenant scope that always returns to admin mode

**Files:**
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/ITenantContextSwitcher.cs`
- Modify: `src/ONEVO.Infrastructure/Identity/Tenancy/TenantContextSwitcher.cs`
- Test: `tests/ONEVO.Tests.Integration/Tenancy/TenantContextSwitcherAdminRestoreTests.cs` (SQL Server, same fixture style as `BaseForgotPasswordRlsIntegrationTests`)

**Interfaces:**
- Produces: `Task SwitchToAdminModeAsync(CancellationToken ct = default)` on `ITenantContextSwitcher`; extension `Task<T> RunInTenantAsync<T>(this ITenantContextSwitcher switcher, TenantRegistryEntry tenant, Func<Task<T>> work, CancellationToken ct = default)` in `src/ONEVO.Application/Common/ServiceInterfaces/TenantContextSwitcherExtensions.cs` that switches, runs `work`, and calls `SwitchToAdminModeAsync` in `finally`.

- [ ] **Step 1: Write the failing tests**
  - `RunInTenantAsync_inside_transaction_reads_tenant_rows_then_restores_admin`: inside `ExecuteInTransactionAsync`, in admin mode, `RunInTenantAsync(tenantA, …)` sees only tenant A's `work_tasks`; afterwards `SESSION_CONTEXT(N'app.tenant_context_mode')` is `admin` and `ITenantContext.ContextMode == TenantContextMode.Admin`.
  - `RunInTenantAsync_restores_admin_when_work_throws`: work throws `InvalidOperationException`; after the catch the mode is `admin`.
- [ ] **Step 2: Run, expect FAIL** — `dotnet test tests/ONEVO.Tests.Integration --filter TenantContextSwitcherAdminRestoreTests` → compile error / missing member.
- [ ] **Step 3: Implement** `SwitchToAdminModeAsync`: inside a transaction run `TenantRlsInterceptor.SetTenantCommand(connection, string.Empty, "admin")` on the current transaction and call `IWritableTenantContext.SetAdminMode()`; outside a transaction close the connection (as `SwitchToTenantAsync` does) and call `SetAdminMode()`. Add the extension.
- [ ] **Step 4: Run, expect PASS** (same command).
- [ ] **Step 5: Commit** — `feat(tenancy): tenant scope that always restores admin mode`

---

### Task 2: Incident schema, permissions and lifecycle rules

**Files:**
- Create: `src/ONEVO.Domain/Features/DevPlatform/Incidents/Entities/` — `Incident.cs`, `IncidentResponder.cs`, `IncidentAffectedTenant.cs`, `IncidentTicketLink.cs`, `IncidentTimelineEvent.cs`, `IncidentCustomerUpdate.cs`, `IncidentWorkLink.cs`, `IncidentPostmortem.cs`, `IncidentSettings.cs`
- Create: `src/ONEVO.Domain/Features/DevPlatform/Incidents/IncidentConstants.cs` (classes `IncidentSeverities`, `IncidentStatuses`, `IncidentWorkLinkKinds`, `IncidentWorkLinkStatuses`, `IncidentTimelineEventTypes`)
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Helpers/IncidentRules.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Incidents/*Configuration.cs` (one per entity)
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (DbSets), `src/ONEVO.Infrastructure/Persistence/Rls/RlsTableRegistry.cs`, `tests/ONEVO.Tests.Integration/SqlServerMigration/SchemaParityTests.cs`
- Modify: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Helpers/PlatformPermissionCatalog.cs`
- Migration: `AddIncidentManagement`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/IncidentRulesTests.cs`

**Interfaces:**
- Produces tables (snake_case columns): `incidents` (id, number int unique, title ≤200, description ≤10000, severity, status, owner_platform_user_id, declared_by_platform_user_id, detected_at, acknowledged_at, first_update_sent_at, resolved_at, closed_at, customer_update_covered_status null, announcement_id null, created_at, updated_at); `incident_responders` (incident_id, platform_user_id; PK both); `incident_affected_tenants` (incident_id, tenant_id, added_at, added_by_platform_user_id; PK both; RLS Tenant); `incident_ticket_links` (incident_id, support_ticket_id unique, tenant_id; RLS Tenant); `incident_timeline_events` (id, incident_id, type, actor_platform_user_id null, details_json ≤4000, occurred_at; append-only); `incident_customer_updates` (id, incident_id, for_status, title, body, tenant_ids_json, channels, sent_by_platform_user_id, sent_at, send_key unique); `incident_work_links` (id, incident_id, kind, idempotency_key unique, status, task_id null, task_short_id null, task_done bool, task_deleted bool, error null, created_at, updated_at); `incident_postmortems` (incident_id PK, summary, root_cause, customer_impact, went_well, went_badly, completed_at null, completed_by_platform_user_id null); `incident_settings` (id fixed single row, internal_tenant_id, project_id, objective_id, bug_category_id, updated_at, updated_by_platform_user_id).
- Check constraints on severity, status, kind and link status values.
- Permission constants `IncidentsRead`, `IncidentsManage` (module key `incidents`, not high risk) and `IncidentsConfigure` (high risk), with descriptions from spec 5.9.
- `IncidentRules`: `bool CanTransition(string from, string to)` (forward one or more steps; backwards to any earlier open status; `closed` → only `investigating`); `bool PostmortemRequiredToClose(string severity)` (true only for `critical`); `bool IsCustomerUpdatePending(Incident incident)` (`CustomerUpdateCoveredStatus != Status` and status not `closed`); `bool CanCoverWithoutUpdate(string severity)` (true only for `minor`); `string FormatNumber(int n)` → `INC-{n}`.

- [ ] **Step 1: Write the failing tests** in `IncidentRulesTests`: transitions `investigating→identified` true, `investigating→resolved` true, `monitoring→investigating` true, `closed→identified` false, `closed→investigating` true, `resolved→closed` true; postmortem required for critical only; pending when covered status differs, not pending when closed; `CanCoverWithoutUpdate("minor")` true, `"major"` false; `FormatNumber(12) == "INC-12"`.
- [ ] **Step 2: Run, expect FAIL** — `dotnet test tests/ONEVO.Tests.Unit --filter IncidentRulesTests`.
- [ ] **Step 3: Implement** entities, constants, rules, configurations, DbSets, registry and parity entries, permission constants; add the migration with `RlsSql.CreatePolicy` for `incident_affected_tenants` and `incident_ticket_links`.
- [ ] **Step 4: Run, expect PASS** — the unit filter above, then `dotnet test tests/ONEVO.Tests.Architecture` (all green except failures already present on `development`, which must not grow).
- [ ] **Step 5: Commit** — `feat(incidents): schema, permissions and lifecycle rules`

---

### Task 3: Declare, read and list incidents

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Commands/DeclareIncident.cs` (command + handler), `Queries/IncidentQueries.cs` (list + get + timeline), `DTOs/IncidentDtos.cs`, `Mappers/IncidentMapper.cs`, `RepositoryInterfaces/IIncidentRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/Incidents/EfIncidentRepository.cs`
- Create: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Incidents/AdminIncidentsController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/DeclareIncidentTests.cs`, `IncidentQueryTests.cs`

**Interfaces:**
- Produces: `DeclareIncidentCommand(string? Title, string? Description, string? Severity, Guid? OwnerPlatformUserId, IReadOnlyList<Guid>? ResponderIds, IReadOnlyList<Guid>? AffectedTenantIds, Guid? FromSupportTicketId, DateTimeOffset? DetectedAt, Guid ActorPlatformUserId) : IRequest<Result<IncidentDto>>, IAuditedCommand<Result<IncidentDto>>`, audit action `incident.declared`.
- `IncidentDto(Guid Id, string Number, string Title, string Severity, string Status, Guid? OwnerPlatformUserId, string? OwnerName, IReadOnlyList<Guid> ResponderIds, int AffectedTenantCount, bool CustomerUpdatePending, bool FixReady, DateTimeOffset DetectedAt, DateTimeOffset? AcknowledgedAt, DateTimeOffset? FirstUpdateSentAt, DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt)`; `IncidentDetailDto` adds `Description`, affected tenants `(Guid TenantId, string Name)`, ticket links, work links, latest customer update.
- `ListIncidentsQuery(string? Status, string? Severity, Guid? OwnerId, bool? Open, int? Page, int? PageSize)` → `SupportPageDto<IncidentDto>` (reuse paging rules `SupportTicketRules.NormalizePaging`); `GetIncidentQuery(Guid Id)`; `GetIncidentTimelineQuery(Guid Id)` → events oldest first.
- Routes: `POST admin/v1/incidents` (manage), `GET admin/v1/incidents` (read), `GET admin/v1/incidents/{id}` (read), `GET admin/v1/incidents/{id}/timeline` (read).
- `IIncidentNotifier` (defined in Task 5) is called after commit: `Task IncidentDeclaredAsync(Guid incidentId, CancellationToken ct)`. In this task inject it and assert the call; Task 5 implements it.

- [ ] **Step 1: Write the failing tests**: declare rejects blank title (400), invalid severity (400), unknown support ticket (404); declare from ticket links the ticket, adds its tenant to affected tenants, sets status `investigating`, `detected_at` = request value or now, number = previous max + 1, writes a `declared` timeline event, calls `IncidentDeclaredAsync`; `DescribeAudit` contains number, severity, affected tenant count and not the description; list filters by open (status ≠ closed); get returns 404 for unknown id.
- [ ] **Step 2: Run, expect FAIL** — `dotnet test tests/ONEVO.Tests.Unit --filter "DeclareIncidentTests|IncidentQueryTests"`.
- [ ] **Step 3: Implement** handlers, repository (number allocation inside the transaction via `MAX(number)+1` with a unique index retry once on conflict), mapper, controller.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(incidents): declare, list and read incidents`

---

### Task 4: Lifecycle, ownership, affected tenants and links

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Commands/IncidentLifecycleCommands.cs`, `Commands/IncidentMembershipCommands.cs`
- Modify: `AdminIncidentsController.cs`, `IIncidentRepository.cs`, `EfIncidentRepository.cs`
- Modify: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Support/AdminSupportTicketsController.cs` (incident id on ticket read model only; no new route there)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/IncidentLifecycleTests.cs`, `IncidentMembershipTests.cs`

**Interfaces (all `IAuditedCommand<Result<IncidentDto>>`, all write a timeline event):**
- `ChangeIncidentStatusCommand(Guid Id, string? Status, Guid ActorPlatformUserId)` — action `incident.status_changed`; sets `resolved_at` on first `resolved`, `closed_at` on `closed`; refuses `closed` for critical without a completed postmortem (409 "Complete the postmortem before closing a Critical incident"); never sends anything.
- `ChangeIncidentSeverityCommand(Guid Id, string? Severity, Guid ActorPlatformUserId)` — action `incident.severity_changed`; raising severity calls `IIncidentNotifier.SeverityRaisedAsync`.
- `AcknowledgeIncidentCommand(Guid Id, Guid ActorPlatformUserId)` — action `incident.acknowledged`; sets `acknowledged_at` once (second call is a no-op success).
- `UpdateIncidentDetailsCommand(Guid Id, string? Title, string? Description, Guid? OwnerPlatformUserId, IReadOnlyList<Guid>? ResponderIds, Guid ActorPlatformUserId)` — action `incident.updated`.
- `AddIncidentAffectedTenantCommand(Guid Id, Guid TenantId, Guid Actor…)` / `RemoveIncidentAffectedTenantCommand(...)` — actions `incident.tenant_added` / `incident.tenant_removed`; removal calls `IIncidentAnnouncementService.RemoveTargetAsync(incidentId, tenantId)` (Task 7) in the same transaction.
- `LinkSupportTicketCommand(Guid Id, Guid SupportTicketId, Guid Actor…)` — action `incident.ticket_linked`; adds the ticket's tenant; 409 if the ticket is linked to another incident.
- `AddIncidentNoteCommand(Guid Id, string? Note, Guid Actor…)` — action `incident.note_added`; note stored only in the timeline `details_json`, never in audit details.
- Routes: `PUT {id}/status`, `PUT {id}/severity`, `POST {id}/acknowledge`, `PUT {id}`, `POST {id}/affected-tenants`, `DELETE {id}/affected-tenants/{tenantId}`, `POST {id}/tickets`, `POST {id}/notes` — all `platform.incidents.manage`.
- No GET mutates data (spec 5.2): reading an incident never sets `acknowledged_at`.

- [ ] **Step 1: Write the failing tests** for each command's happy path, invalid transition (400 with the allowed list), critical close without postmortem (409), acknowledge idempotence, remove-tenant calling `RemoveTargetAsync`, ticket already linked elsewhere (409), and that `GetIncidentQuery` never changes `acknowledged_at`.
- [ ] **Step 2: Run, expect FAIL** — `--filter "IncidentLifecycleTests|IncidentMembershipTests"`.
- [ ] **Step 3: Implement.** `IIncidentAnnouncementService` is injected as an interface here; Task 7 implements it.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(incidents): lifecycle, ownership, affected tenants and links`

---

### Task 5: Team alerts by severity

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Services/IIncidentNotifier.cs`
- Create: `src/ONEVO.Infrastructure/Services/DevPlatform/Incidents/IncidentNotifier.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/OutboxHandlers/IncidentTeamAlertEmailOutboxHandler.cs`
- Modify: `OutboxMessageTypes` (add `IncidentTeamAlertEmail = "incident_team_alert_email"`)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/IncidentNotifierTests.cs`

**Interfaces:**
- `IIncidentNotifier`: `Task IncidentDeclaredAsync(Guid incidentId, CancellationToken ct)`, `Task SeverityRaisedAsync(Guid incidentId, CancellationToken ct)`.
- Recipients: critical → every active platform user holding `platform.incidents.read` (resolved through `IPlatformPermissionResolver`/platform user repository); major → owner + responders; minor → owner. No recipients → `Support:AlertEmail` (reuse `SupportNotifierOptions`); still none → warning log, no exception.
- Enqueues one `incident_team_alert_email` outbox message per recipient with payload `{ incidentId, number, title, severity, status, recipientEmail, adminUrl }`; the handler sends via `IEmailService` with subject `[{SEVERITY}] {number}: {title}`. Admin URL from `Email:AdminConsoleBaseUrl` + `/incidents/{id}`.

- [ ] **Step 1: Write the failing tests**: recipient selection per severity; fallback to alert email; no recipients and no alert email logs a warning and enqueues nothing; payload contains no description.
- [ ] **Step 2: Run, expect FAIL** — `--filter IncidentNotifierTests`.
- [ ] **Step 3: Implement** notifier, outbox type, handler (idempotent: sending twice is acceptable for an alert, but skip when the incident no longer exists).
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(incidents): severity-based team alerts`

---

### Task 6: Announcements for selected tenants

**Files:**
- Modify: `src/ONEVO.Domain/Features/DevPlatform/Support/Entities/PlatformAnnouncement.cs` (add `AudienceSelectedTenants = "selected_tenants"`, `Guid? IncidentId`, collection `Targets`)
- Create: `src/ONEVO.Domain/Features/DevPlatform/Support/Entities/PlatformAnnouncementTarget.cs` (`announcement_id`, `tenant_id`; RLS Tenant)
- Modify: `PlatformAnnouncementConfiguration.cs` (audience check constraint gains `selected_tenants`), add `PlatformAnnouncementTargetConfiguration.cs`
- Modify: `IPlatformAnnouncementRepository.ListActiveAsync` → add `Guid? tenantId`; `EfPlatformAnnouncementRepository`; `GetActiveAnnouncementsQuery(string Audience, Guid? TenantId = null)`; tenant `AnnouncementsController.Active` passes the current tenant id
- Modify: `AnnouncementRules.Validate` — the generic admin announcement create/update rejects `selected_tenants` (400 "selected_tenants announcements are managed by incidents")
- Migration: `AddAnnouncementTenantTargets` (+ registry and parity entries)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Support/ActiveAnnouncementTargetingTests.cs`, `tests/ONEVO.Tests.Integration/Support/AnnouncementTargetRlsTests.cs`

**Interfaces:**
- Produces: active query returns `all`, `tenants`, and `selected_tenants` rows only where a target row exists for the caller's tenant.

- [ ] **Step 1: Write the failing tests**: tenant A sees a `selected_tenants` announcement targeting A; tenant B does not; admin-console audience never returns `selected_tenants`; generic create with `selected_tenants` is rejected; RLS integration test: in tenant B mode, selecting `platform_announcement_targets` returns no rows for A.
- [ ] **Step 2: Run, expect FAIL** — `--filter "ActiveAnnouncementTargetingTests|AnnouncementTargetRlsTests"` on both test projects.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(support): announcements targeted at selected tenants`

---

### Task 7: Customer updates (templates, Send, no-update, banner rule, email)

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Commands/CustomerUpdateCommands.cs`, `Queries/CustomerUpdateDraftQuery.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Services/IIncidentAnnouncementService.cs` + `IncidentAnnouncementService.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/OutboxHandlers/IncidentCustomerUpdateEmailOutboxHandler.cs`; add `OutboxMessageTypes.IncidentCustomerUpdateEmail = "incident_customer_update_email"`
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs` — four templates
- Modify: `src/ONEVO.Domain/.../Tenant` entity + configuration — `IncidentContactEmail` (nullable, ≤254); migration `AddTenantIncidentContact`
- Create: `src/ONEVO.Api/Controllers/Tenant/Support/IncidentContactController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/CustomerUpdateTests.cs`, `IncidentAnnouncementServiceTests.cs`, `IncidentContactTests.cs`

**Interfaces:**
- Template codes exactly `incident_update_investigating`, `incident_update_identified`, `incident_update_monitoring`, `incident_update_resolved`; placeholders `{{incident_number}}`, `{{incident_title}}`, `{{impact}}`; mail subject + body set, `InAppEnabled = false`. Update any reference-data parity expectations in the same commit.
- `GetCustomerUpdateDraftQuery(Guid IncidentId, string? ForStatus)` → `CustomerUpdateDraftDto(string ForStatus, string Title, string Body, IReadOnlyList<(Guid TenantId, string Name)> Targets, IReadOnlyList<Guid> TenantsNotYetTold)`; `ForStatus` defaults to the current status.
- `SendCustomerUpdateCommand(Guid IncidentId, string? ForStatus, string? Title, string? Body, bool Banner, bool Email, string SendKey, Guid ActorPlatformUserId)` : `IAuditedCommand<Result<IncidentDto>>`, action `incident.customer_update_sent`, audit details = number, for_status, target count, channels. Rules: 400 when no affected tenants; `SendKey` unique — a repeated key returns the existing result without side effects; replaces the banner target list with the current affected tenants (`IIncidentAnnouncementService.PublishAsync(incident, title, body)`, creating the incident's announcement on first send); for `resolved` sets `EndsAt` = now + 24 h; enqueues one email message per recipient; sets `first_update_sent_at` once; sets `customer_update_covered_status` = `ForStatus` when it equals the current status; writes a timeline event.
- `MarkNoCustomerUpdateRequiredCommand(Guid IncidentId, Guid Actor…)` — action `incident.no_customer_update`; only for `minor` (400 otherwise); covers the current status.
- `IIncidentAnnouncementService`: `Task PublishAsync(Incident incident, string title, string body, CancellationToken ct)`, `Task RemoveTargetAsync(Guid incidentId, Guid tenantId, CancellationToken ct)`. Banner rule (spec 5.5): targets after Publish = current affected tenants; RemoveTarget deletes that tenant's target row immediately; adding a tenant changes nothing until the next Publish.
- Email recipients per tenant: `IncidentContactEmail` if set; otherwise active users of that tenant holding `support:manage` (same rule the support notifier uses).
- Tenant routes: `GET api/v1/settings/incident-contact`, `PUT api/v1/settings/incident-contact` (`RequirePermission("support:manage")`), body `{ "email": string|null }`, validated as an email address.
- Admin routes: `GET admin/v1/incidents/{id}/customer-update-draft?for_status=` (read), `POST {id}/customer-updates` (manage), `POST {id}/no-customer-update` (manage).

- [ ] **Step 1: Write the failing tests** — including the Review Focus cases: repeated `SendKey` produces one email batch and one timeline event; no affected tenants → 400; removal prunes the target; a tenant added after a send is listed in `TenantsNotYetTold` and is not a target until the next send; no-update rejected for major; contact email used before `support:manage` users; resolved banner ends after 24 h.
- [ ] **Step 2: Run, expect FAIL** — `--filter "CustomerUpdateTests|IncidentAnnouncementServiceTests|IncidentContactTests"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS**, plus `dotnet test tests/ONEVO.Tests.Unit --filter ReferenceData` stays green.
- [ ] **Step 5: Commit** — `feat(incidents): customer updates with targeted banner and email`

---

### Task 8: Internal tenant flag and incident settings

**Files:**
- Modify: Tenant entity + configuration — `IsInternal` (bool, default false); migration `AddTenantIsInternal`
- Create: `ops/sqlserver/mark-internal-tenant.sql` (sets `is_internal = 1` for a slug passed with `-v TENANT_SLUG=`; prints the row; never touches other tenants)
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Commands/IncidentSettingsCommands.cs` (+ query)
- Create: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Incidents/AdminIncidentSettingsController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/IncidentSettingsTests.cs`

**Interfaces:**
- `GetIncidentSettingsQuery()` → `IncidentSettingsDto(Guid? InternalTenantId, string? InternalTenantName, Guid? ProjectId, Guid? ObjectiveId, Guid? BugCategoryId, bool IsValid, string? Problem)`; `IsValid` re-checks the same rules as update.
- `UpdateIncidentSettingsCommand(Guid InternalTenantId, Guid ProjectId, Guid ObjectiveId, Guid BugCategoryId, Guid Actor…)` : audited `incident.settings_updated`. Rules: tenant exists and `IsInternal` (else 400 "Only a tenant flagged as internal can be used"); project, objective and category belong to that tenant and are active (checked inside `RunInTenantAsync`); category kind is `bug`.
- `GET/PUT admin/v1/incidents/settings` — GET needs `platform.incidents.read`, PUT needs `platform.incidents.configure`.
- `IsInternal` is never writable through any API.

- [ ] **Step 1: Write the failing tests**: non-internal tenant rejected; project from another tenant rejected; non-bug category rejected; valid save; GET reports `IsValid = false` with a reason once the objective is archived.
- [ ] **Step 2: Run, expect FAIL** — `--filter IncidentSettingsTests`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(incidents): internal tenant flag and settings`

---

### Task 9: Work Management task fields for incident links

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs` — `SourceIncidentId` (Guid?), `SourceIncidentLinkKind` (string?), `ExternalRequestKey` (string?, ≤100)
- Modify: `WorkTaskConfiguration.cs` — unique filtered index `(tenant_id, external_request_key) WHERE external_request_key IS NOT NULL`
- Modify: `TaskCreateInput` — add `Guid? SourceIncidentId = null, string? SourceIncidentLinkKind = null, string? ExternalRequestKey = null, string? SourceIncidentNumber = null, string? SourceIncidentSeverity = null`
- Modify: `ITaskWriteService` + implementation — `CreateAsync` copies the three fields; add `Task<WorkTask?> FindByExternalRequestKeyAsync(Guid tenantId, string key, CancellationToken ct)`
- Modify: task detail and board list read models/DTOs — `SourceIncident { Id, Number, Severity, LinkKind }` (number and severity read from `incidents` in admin-free fashion: stored as `SourceIncidentNumber`/`SourceIncidentSeverity` snapshot columns on the task, set at creation, so tenant-mode reads never touch platform tables)
- Modify: board/list query — optional `incident_linked=true` filter
- Migration: `AddWorkTaskIncidentLink`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskIncidentLinkTests.cs`

**Interfaces:**
- Produces the fields and `FindByExternalRequestKeyAsync` used by Task 10, and the snapshot columns `SourceIncidentNumber` (≤20) and `SourceIncidentSeverity` (≤10).

- [ ] **Step 1: Write the failing tests**: create copies the source fields and snapshots; `FindByExternalRequestKeyAsync` finds by key within the tenant only; task DTO exposes `SourceIncident`; filter returns only linked tasks.
- [ ] **Step 2: Run, expect FAIL** — `--filter TaskIncidentLinkTests`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS**, plus the existing Work Management unit tests: `--filter FullyQualifiedName~WorkManagement`.
- [ ] **Step 5: Commit** — `feat(work): incident link fields on tasks`

---

### Task 10: Guarded writer and escalation with idempotent retry

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Services/IInternalTenantWorkWriter.cs`
- Create: `src/ONEVO.Infrastructure/Services/DevPlatform/Incidents/InternalTenantWorkWriter.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Commands/EscalationCommands.cs`
- Create: `src/ONEVO.Infrastructure/BackgroundJobs/IncidentWorkLinkRecoveryJob.cs` (hosted, every 5 minutes)
- Modify: `AdminIncidentsController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/EscalationTests.cs`, `tests/ONEVO.Tests.Integration/Incidents/InternalTenantWorkWriterRlsTests.cs`

**Interfaces:**
- `IInternalTenantWorkWriter.CreateTaskAsync(InternalWorkTaskRequest request, CancellationToken ct) : Task<Result<InternalWorkTaskRef>>` with `InternalWorkTaskRequest(Guid IncidentId, string IncidentNumber, string IncidentSeverity, string LinkKind, string IdempotencyKey, string Title, string? Description, Guid? AssigneeEmployeeId, string ActorEmail)` and `InternalWorkTaskRef(Guid TaskId, string ShortId)`.
  - Loads `incident_settings`; refuses unless the tenant is `IsInternal` and equals the setting (403 "Escalation target is not the configured internal tenant"); stale configuration → 409 (Review Focus).
  - Inside `RunInTenantAsync(internalTenant, …)`: `FindByExternalRequestKeyAsync` first (return it if found); resolve the creator employee by `ActorEmail` (none → 400 "You have no employee record in the internal tenant with this email. Ask to be added first."); `ITaskWriteService.CreateAsync` directly (no approval engine, no module-manager check) with `ExternalRequestKey = IdempotencyKey`, the source fields and snapshots; assign `AssigneeEmployeeId` in the same operation through the existing assignment write used by the task write path.
- `EscalateIncidentCommand(Guid IncidentId, string SubmissionId, IReadOnlyList<EscalationBugInput> Bugs, Guid ActorPlatformUserId)` with `EscalationBugInput(string Title, string? Description, Guid? AssigneeEmployeeId)` : audited `incident.escalated` (details: number, bug count). Flow per spec 5.6: a repeated `SubmissionId` for the incident returns the existing links; otherwise, for each bug, the server generates a key (`{incidentId:N}-{Guid.NewGuid():N}`), writes an `engineering_bug` link as `pending`, commits, calls the writer, marks `created` (task id + short id) or `failed` (error), writes timeline events. Description prefix: `Incident {number} · {severity} · {status} · {n} affected tenant(s)` (no names).
- `RetryIncidentWorkLinkCommand(Guid IncidentId, Guid LinkId, Guid Actor…)` : audited `incident.work_link_retried`; only for `failed` or `pending` older than 2 minutes; reuses the link's key.
- `IncidentWorkLinkRecoveryJob`: `pending` links older than 5 minutes → look up by key in the internal tenant → `created` or `failed` ("Not created; retry").
- `ListInternalTenantEmployeesQuery(string? Search)` for the assignee picker (read permission): id, name, email; runs in `RunInTenantAsync`.
- Routes: `POST {id}/escalations` (manage), `POST {id}/work-links/{linkId}/retry` (manage), `GET admin/v1/incidents/internal-employees?search=` (read).

- [ ] **Step 1: Write the failing tests**: escalate creates N links with distinct keys; repeated submission id returns the same links; writer called with the stored key on retry; actor with no matching employee → link `failed` with the exact message; non-internal or unconfigured tenant → refused before any link; stale objective → 409 and no link; description has the count and no tenant names; recovery job resolves a pending link to `created` when the task exists. Integration (SQL Server, restricted runtime login): the writer can create a task in the internal tenant, cannot when the configured tenant is not flagged, and leaves the connection in admin mode afterwards.
- [ ] **Step 2: Run, expect FAIL** — unit `--filter EscalationTests`, integration `--filter InternalTenantWorkWriterRlsTests`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(incidents): guarded escalation into the internal tenant`

---

### Task 11: Sync back from Work Management

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Interceptors/IncidentTaskStatusInterceptor.cs` (registered beside `DomainEventDispatchInterceptor` in `ApplicationDbContext.OnConfiguring`)
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/OutboxHandlers/IncidentWorkTaskChangedOutboxHandler.cs`; add `OutboxMessageTypes.IncidentWorkTaskChanged = "incident_work_task_changed"`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/IncidentSyncTests.cs`, `tests/ONEVO.Tests.Integration/Incidents/IncidentTaskStatusInterceptorTests.cs`

**Interfaces:**
- The interceptor, on `SavingChanges`, for each `WorkTask` entry with `SourceIncidentId != null` whose `StatusId` changed or that was soft-deleted/restored, adds an outbox row (same unit of work) with payload `{ taskId, incidentId, linkKind, statusCategory, deleted }`. This covers every status path (`MoveTaskStatus`, `ClockInTask`, approval appliers, bulk, board drag) without touching them.
- Handler (idempotent): updates `incident_work_links.task_done` / `task_deleted`; writes a timeline event only when the done/deleted value actually changes; `FixReady` = every `engineering_bug` link is `created`, not deleted, and done (computed in the mapper from link rows); `postmortem_action` links are ignored for FixReady.

- [ ] **Step 1: Write the failing tests**: done → link done and FixReady true when it is the only bug; one of two bugs done → FixReady false; reopen clears; deleted bug → FixReady false and a `bug_deleted` timeline event (Review Focus); postmortem action done does not change FixReady; duplicate delivery writes one timeline event; integration: moving a linked task's status through `MoveTaskStatusCommand` writes exactly one outbox row of the new type.
- [ ] **Step 2: Run, expect FAIL** — `--filter "IncidentSyncTests|IncidentTaskStatusInterceptorTests"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(incidents): sync bug status back to incidents`

---

### Task 12: Postmortem

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Incidents/Commands/PostmortemCommands.cs`
- Modify: `AdminIncidentsController.cs`, `IncidentDetailDto` (postmortem section)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Incidents/PostmortemTests.cs`

**Interfaces:**
- `SavePostmortemCommand(Guid IncidentId, string? Summary, string? RootCause, string? CustomerImpact, string? WentWell, string? WentBadly, Guid Actor…)` — draft save; allowed only at `resolved`; audited `incident.postmortem_saved` (no text in details).
- `CompletePostmortemCommand(Guid IncidentId, string SubmissionId, IReadOnlyList<EscalationBugInput> Actions, Guid Actor…)` — requires summary and root cause (400 otherwise); sets `completed_at`; creates each action as a `postmortem_action` link through the same pending → writer → created/failed flow as Task 10 (same retry and recovery); audited `incident.postmortem_completed` (details: action count).
- Routes: `PUT {id}/postmortem`, `POST {id}/postmortem/complete` (manage).
- Closing a critical incident checks `completed_at` (Task 4 rule).

- [ ] **Step 1: Write the failing tests**: save only at resolved; complete requires summary and root cause; actions become `postmortem_action` links; critical can close after completion; repeated submission id creates no duplicate actions.
- [ ] **Step 2: Run, expect FAIL** — `--filter PostmortemTests`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(incidents): postmortem with action tasks`

---

### Task 13: Whole-branch verification

**Files:** none new (fix only what the runs reveal).

- [ ] **Step 1:** `dotnet build -c Release` — 0 warnings, 0 errors (repo's zero-warning rule).
- [ ] **Step 2:** `dotnet test tests/ONEVO.Tests.Unit` — all pass.
- [ ] **Step 3:** `dotnet test tests/ONEVO.Tests.Architecture` — all pass, in particular the F-07 audit-coverage tests for every new admin command and the RLS registry/migration coverage tests.
- [ ] **Step 4:** `dotnet test tests/ONEVO.Tests.Integration` against SQL Server (needs ~12 GB free disk) — all pass, including `SchemaParityTests` and the incident RLS tests.
- [ ] **Step 5:** Commit any fixes; push `feat/incident-management-backend`. No PR unless Dabi asks.
