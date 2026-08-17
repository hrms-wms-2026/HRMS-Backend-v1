# Employee onboarding finalization and position access

## Position access change

Position creation now accepts optional `RoleId` and `RequiresApproval`. A supplied role is
looked up tenant-scoped; a missing or cross-tenant role is rejected. `RequiresApproval` is
required when a role is supplied. The create transaction adds a `PositionAccessTemplate` for
the newly created position. No role is created or assigned by default. The existing Set Position
Access endpoint remains unchanged.

## Inventory versus backend implementation

The Phase 1 inventory defines `checklist_templates`, `employee_checklist_tasks`, and
`access_grant_requests`. The backend search found position access templates and position
assignments, but no EF/domain/repository implementation for checklist templates, employee
checklist tasks, or access-grant requests. These are implementation gaps, not claims that the
product schema is absent.

## Finalization status

Finalization was not added in this change. It needs a dedicated transactional employee issuance
service that creates the pending auth user, employee, optional position assignment, employee
invitation token, and outbox record in one unit of work. Existing generic invitation acceptance
handlers intentionally reject employee-onboarding tokens because they may apply a position's
default role; that is not a safe employee access policy. No owner/admin role is assigned.

## Verification

`dotnet build src\\ONEVO.Api\\ONEVO.Api.csproj --no-restore --verbosity minimal` passed with
an isolated output directory. The normal output remains locked by a running local process.
There is one pre-existing nullable warning in `AdminAuthController.cs:62`.

Focused position/finalization tests were not run because the requested finalization implementation
and its test seam have not been added. No frontend changes, commit, or push occurred.
