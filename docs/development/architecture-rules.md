# Architecture rules

Where new code goes, and when a file is too big. Started by task 25
(`docs/Improvements/person-4-deployment-and-architecture/task-25 Split service registration by module.md`).

## Where a new piece goes

| What | Code | Registration |
|---|---|---|
| Command, query, handler | `src/ONEVO.Application/Features/<Module>/Commands|Queries/<Name>/` | none: MediatR scans the Application assembly |
| Validator | next to its command, `<Name>CommandValidator` | none: `AddValidatorsFromAssembly` |
| Repository interface | `src/ONEVO.Application/Features/<Module>/RepositoryInterfaces/` | |
| Repository (EF) | `src/ONEVO.Infrastructure/Persistence/Repositories/<Module>/` | the module's file in `src/ONEVO.Infrastructure/Registration/` |
| Application service (no infrastructure) | `src/ONEVO.Application/Features/<Module>/Services/` | the module's registration file, or `ONEVO.Application/DependencyInjection.cs` if it needs nothing from Infrastructure |
| Infrastructure service, external client | `src/ONEVO.Infrastructure/Services/<Module>/` or `ExternalServices/<Provider>/` | the module's registration file |
| Recurring background job | `src/ONEVO.Infrastructure/Services/<Module>/<Name>Job.cs`, running its work through `ExclusiveJobRunner` (task 13) | `Registration/RecurringJobsRegistration.cs` with `AddBackgroundJob<T>`, and a row in `docs/deployment/background-jobs.md` |
| Seeder | `src/ONEVO.Infrastructure/Persistence/Seeders/` | `Registration/SeedersRegistration.cs`, inside the seeding lock |
| Inbox plug-in (kind, source, chat subject, reverter, escalation) | the module's `Inbox/` folder in Application | `Registration/InboxRegistration.cs` |
| My Team action source | `src/ONEVO.Application/Features/Dashboard/Team/Sources/` | `Registration/DashboardRegistration.cs` |
| Hub, realtime notifier, presence | `src/ONEVO.Api/Hubs/` | `src/ONEVO.Api/Extensions/RealtimeExtensions.cs` |

## Registration files

`AddInfrastructure` (`src/ONEVO.Infrastructure/DependencyInjection.cs`) only calls one method per area, each in
its own file under `src/ONEVO.Infrastructure/Registration/`:

`DatabaseRegistration`, `IdentityRegistration`, `DeveloperPlatformRegistration`, `OrgStructureRegistration`,
`TimeAttendanceRegistration`, `LeaveRegistration`, `MonitoringRegistration`, `CoreHrRegistration`,
`InboxRegistration`, `WorkManagementRegistration`, `StorageRegistration`, `CalendarIntegrationsRegistration`,
`MessagingRegistration`, `RequestContextRegistration`, `DashboardRegistration`, `RecurringJobsRegistration`,
`SharedStateSessionsRegistration`, `SeedersRegistration`, then `ConfigurationStartupValidator`.

**Order.** DI keeps registrations of the same service type in the order they were added: that is the order an
`IEnumerable<T>` gives (inbox sources and their filter options, outbox handlers, team action sources, escalation
policies where the first match wins) and the order hosted services start. So:

- A registration whose service type is registered only once can go anywhere in its module's file.
- A registration of a type that several modules register goes to the file that already holds that list
  (`InboxRegistration`, `DashboardRegistration`, `RecurringJobsRegistration`, `SeedersRegistration`) at the
  position it should take in the list.
- Do not reorder the calls in `AddInfrastructure`. The seeders must stay after the jobs and shared state, inside
  `ReferenceSeedingLock.Acquire` / `.Release`, and `ConfigurationStartupValidator` last.

**Split check (2026-11-20).** The registrations were compared before and after the split, without and with Redis
configured: 717 and 721 registrations, the same set, and for every service type the same implementations in the
same order. The one exception: ASP.NET's `DataProtectionHostedService` now starts before the recurring jobs instead
of between two of them. It only loads the key ring, so starting it earlier changes nothing. Three architecture tests
that read `DependencyInjection.cs` as text now read the `Registration/` files too.

## File size

- Over **400 lines**: look for a second job hiding in the file and split it when you next change it.
- Over **600 lines**: split before adding more. One handler, repository area or controller resource per file.
- Seeders full of data and EF migrations are exempt.

### Files to split (follow-up tasks, not done in task 25)

Sizes on 2026-11-20.

| File | Lines | Suggested split |
|---|---|---|
| `Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` | ~1,055 | By concern: reads for lists and search, profile and identity reads, hierarchy and reporting lines, writes. Keep `IEmployeeRepository` as is; make the class `partial` first, then move each area to its own file |
| `Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs` | 822 | Per wizard step (personal, job, compensation, documents) plus finalize; one shared draft loader |
| `Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` | 747 | One file per query handler (the file holds several) |
| `Api/Controllers/Tenant/CoreHr/EmployeesController.cs` | ~695 | By resource: employee CRUD and list, profile, documents, access and invitations |
| `Application/Features/TimeAttendance/Inbox/AttendanceInboxSourceProvider.cs` | 689 | Per kind (correction, work mode, location, device) behind one provider that delegates, as `AttendanceAlertInbox` already does for alerts |
| `Application/Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs` | 662 | Submit, approve or reject, revert |
| `Application/Features/Leave/Inbox/LeaveInboxSourceProvider.cs` | 622 | List and counts, detail, actions |
| `Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs` | 588 | Visibility, approval scope, escalation targets |
| `Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs` | 587 | Reads, writes, people chart queries |
| `Application/Features/TimeAttendance/Commands/WorkAreaChangeRequests/WorkAreaChangeRequestWorkflow.cs` | 585 | As corrections |
| `Infrastructure/Services/Monitoring/ActivityMonitoring/MonitoringToggleResolverService.cs` | 579 | Toggle resolution, caching |

## Who a row is about: employee_id or user_id (task 42)

- **Business tables refer to a person by `employee_id`.** Tasks, leave, attendance, monitoring, inbox, org structure:
  everything HR and work data is about an employee.
- **`user_id` is only for login, session and security tables**: user accounts, sessions, MFA, invitation and
  password-reset tokens, permission overrides, legal acceptances.
- Do not store both. If a business table needs the login (for example to send a notification), join `employees.user_id`.
  When both are truly needed, write the reason in the entity's XML comment. The four tables that store both today and
  the decision for each: `docs/reports/database/2026-11-20-task-42-concurrency-identity-empty-tables.md`.

## Concurrency tokens (task 42)

A table users edit through a form gets a rowversion token: `builder.HasRowVersionToken()` in its configuration
(`Persistence/SqlServer/RowVersionConfiguration.cs`). Then:

1. Return the version in the detail and update responses: `IRowVersionStore.GetVersionAsync<T>(id)` for a read,
   `GetVersion(entity)` after a save. Add it as a trailing `string? Version = null` so existing callers keep compiling.
2. Accept it on the update: `string? Version = null` on the request and command, and
   `IRowVersionStore.SetExpectedVersion(entity, request.Version)` after the repository's `Update`, before the save.
3. A stale version throws `DbUpdateConcurrencyException`; `ExceptionHandlerMiddleware` returns 409 with
   `type: urn:crownus:error:concurrency-conflict`. The client reloads and reapplies its change.

No version sent means the last save wins, as before. Rows loaded with `AsNoTracking` and then `Update()`d still save:
`ApplicationDbContext.SaveChangesAsync` reads their current version first. Do not add a token to a table that raw SQL
or `ExecuteUpdate` changes on a hot path (`projects` is left out: its task and meeting counters change on every new
task), or every client version goes stale. Nor to a table a background process loads, waits on an external call and
saves back while users edit it (`personal_calendar_events`: meeting links, calendar sync) until that writer updates only
its own columns.

## Checklist: designing a new table (task 42)

- [ ] `tenant_id` column with a foreign key to `tenants`, and the table registered in `RlsTableRegistry` with its policy
      created in the same migration (`RlsSql.CreatePolicy`).
- [ ] Indexes start with `tenant_id`, in the order the queries filter.
- [ ] Clustered key chosen on purpose and written in the configuration comment: the primary key by default, or a
      tenant-first index for a large append-only table read by time (see the 2026-11-15 database design review).
- [ ] A foreign key for every `_id` column, with the delete behaviour chosen on purpose.
- [ ] A check constraint for every status or kind column, built from the constants class
      (`InboxEntryConfiguration.InList` is an example). Use plain `'value'` literals so the in-process test schema accepts it.
- [ ] Real string lengths (`HasMaxLength`), never unbounded `nvarchar(max)` except for documents and JSON.
- [ ] `HasRowVersionToken()` if users edit it through a form (section above).
- [ ] People referenced by `employee_id`, not `user_id`, unless it is a login or security table (identity rule above).
- [ ] `created_at` / `updated_at` as `datetimeoffset`, and soft delete only if the feature needs undelete.
