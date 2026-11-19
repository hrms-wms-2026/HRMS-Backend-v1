# Attendance Corrections Backend Part 1

## Scope and implementation summary

The Backend Part 1 attendance-correction feature is implemented in `HRMS-Backend-v1` only. The implementation reuses the existing tenant, attendance, authority-resolution, unit-of-work, notification dispatcher, permission, and RLS conventions. No frontend files were changed, and no email-only or parallel notification mechanism was introduced.

| Area | Implementation |
|---|---|
| Persistence | `AttendanceCorrection` domain entity, EF configuration, repository, migration, model snapshot, indexes, restrictive foreign keys, and tenant RLS. |
| Request flow | Server-derived employee and legal-entity context; schedule/policy/date/time/break validation; pending or auto-approved status; duplicate pending guard. |
| Approval routing | `IEmployeeAuthorityResolver` with `attendance:approve` and `EmployeeAuthorityPurpose.AttendanceCorrectionApproval`; preview returns approval requirement and approver summary. |
| Review flow | Approve applies the requested correction and recalculates attendance status; reject changes only correction status; cancel is requester-only and pending-only. |
| Reads | Requester history and approval inbox queries with date/status filtering and requester display enrichment where the existing attendance identity query supports it. |
| API | `api/v1/attendance/corrections` preview, create, requester list, approval list, approve, reject, and cancel routes. |
| Tests | Six workflow notification/transaction unit tests, five navigation-metadata test cases, and six architecture/security/migration tests cover the correction feature. |

## Existing notification/outbox integration

Correction notifications are dispatched through the existing `INotificationDispatcher.SendTemplatedAsync` contract. The correction workflow does not insert directly into a new notification table and does not create an email-only path. Approval-required requests use the seeded `attendance_correction_request_created` template and send the message to the resolver-selected approver with the non-technical copy `Attendance correction request from {{employeeName}}.`

Approve, reject, auto-approve, and cancel flows use the existing templated in-app notification contract for requester-facing status messages. The notification call receives `RelatedEntityType = "attendance_correction"` and the correction id as `RelatedEntityId`, matching the existing notification metadata convention. The correction mutation and dispatcher call execute inside the same `IUnitOfWork.ExecuteInTransactionAsync` delegate, and the focused unit test asserts that notification creation occurs while the transaction fake is active.

## Notification destination decision

The existing shared notification response contract has now been extended additively with optional `Destination` metadata; no unrelated notification type or work-management navigation behavior was changed. For an approval-request notification with `RelatedEntityType = "attendance_correction"` and `TemplateCode = "attendance_correction_request_created"`, the mapper returns:

```json
{
  "notificationType": "attendance_correction_request_created",
  "attendanceCorrectionId": "<correction-id>",
  "legalEntityId": null,
  "destinationKey": "attendance_correction_approval",
  "isNavigable": true
}
```

Requester decision and cancellation notifications retain the correction id but are explicitly marked non-navigable until a frontend destination contract exists: `destinationKey = null` and `isNavigable = false`. This is a deliberate contract decision rather than a fabricated route. The backend does not expose technical URL internals and does not assume that the not-yet-built frontend has an attendance-correction detail route.

The existing work-management-only `GetWorkNotificationNavigationQueryHandler` remains unchanged. A future frontend navigation implementation can consume the stable destination key and correction id from the shared notification view model, or a later generic navigation resolver can formalize that mapping without changing notification persistence.

## Inventory-aligned schema note

The Phase 1 inventory did not include an explicit `work_date` column in the draft correction shape. The final implementation adds `work_date` because break-only corrections may contain no clock timestamp from which the target local date can be reconstructed during approval or list reads. This is documented in the entity source and represented in the migration and snapshot rather than relying on an unsafe UTC-date inference.

## Migration application readiness

The design-time architecture requires the elevated `MigrationConnection`; it must not fall back to the restricted runtime `DefaultConnection`. The exact .NET configuration key is:

```powershell
$env:ConnectionStrings__MigrationConnection = "Host=<host>;Port=<port>;Database=<database>;Username=onevo_migrator;Password=<migration-password>"
```

The repository-supported local configuration is the atomic `.env` form, which avoids committing connection strings:

```text
ONEVO_DB_HOST=localhost
ONEVO_DB_PORT=5432
ONEVO_DB_NAME=OnevoDb
ONEVO_DB_MIGRATOR_USER=onevo_migrator
ONEVO_DB_MIGRATOR_PASSWORD=<local-migration-password>
```

After the remaining admin/app password values are supplied, the supported application command is:

```powershell
.\ops\postgres\setup-local-db.ps1 -RunMigrations
```

The underlying EF command executed by that script is:

```powershell
dotnet ef database update --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj
```

With a syntactically valid non-secret `ConnectionStrings__MigrationConnection`, EF successfully discovered `20260824120000_AddAttendanceCorrections` and rendered SQL containing the `attendance_corrections` table, restrictive foreign keys, the pending-only unique index, and tenant RLS policy statements. A live local application attempt reached the repository’s pre-migration role-bootstrap step but stopped at the existing `GRANT onevo_auth_base_login_fn_owner TO onevo_migrator` permission failure in `ops/postgres/local-bootstrap-roles.sql`, before EF database update ran. Therefore, migration source/discovery/DDL readiness is verified, but live application remains blocked by local role-bootstrap permissions rather than by the correction migration.

## Validation performed

| Check | Result |
|---|---:|
| `dotnet build src\\ONEVO.Api\\ONEVO.Api.csproj --configuration Release --no-restore` | Passed with 0 errors and 0 warnings. |
| `dotnet test tests\\ONEVO.Tests.Unit\\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~AttendanceCorrection"` | **11 passed**. |
| `dotnet test tests\\ONEVO.Tests.Architecture\\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | **657 passed**. |
| `git diff --check` | Passed; Git emitted only existing LF/CRLF normalization warnings. |
| EF migration discovery with configured design-time key | Discovered `20260824120000_AddAttendanceCorrections`. |
| EF SQL rendering with configured design-time key | Passed; correction DDL and RLS statements present. |
| Live local migration application | Blocked before EF by existing bootstrap-role permission failure. |

## Remaining frontend contract notes

No frontend files or routes were changed. The backend now exposes additive destination metadata for approval-request notifications through the existing notification view model. Frontend work should implement the `attendance_correction_approval` destination key against the correction approval-inbox experience. Requester approve/reject/cancel notifications are intentionally marked non-navigable until product/frontend work defines a detail destination; no fake route was introduced.
