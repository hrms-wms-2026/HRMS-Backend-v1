# Employee onboarding Phase 1 persistence contracts

## Inventory checked

The implementation follows the Phase 1 inventory for `access_grant_requests`,
`checklist_templates`, and `employee_checklist_tasks`. The access-request model includes
the inventory's additional `user_id`, `action_type`, `target_department_id`,
`effective_from`, and `effective_to` fields. The request shorthand's `status`,
`requested_by_user_id`, `decided_by_user_id`, and `decision_note` map to the inventory's
`approval_status`, `requested_by`, `approved_by`, and `decision_comment` concepts.

## Added persistence contracts

- Tenant-owned Core HR entities: `AccessGrantRequest`, `ChecklistTemplate`, and
  `EmployeeChecklistTask`.
- EF configurations, DbSets, Application repository interfaces, EF repositories, and DI
  registrations.
- `20260810160000_AddEmployeeOnboardingPhase1PersistenceContracts` creates all three tables,
  foreign keys, indexes, a partial unique pending-access-request index, and forced RLS policies.
- `ApplicationDbContextModelSnapshot` includes the tables, columns, indexes, and relationships.

## JSON task behavior and limitation

`tasks_json` is mapped as PostgreSQL `jsonb`. Task instantiation uses edited draft JSON when it
is supplied, otherwise template JSON. The parser requires an array and validates every task's
`title`, `ownerType`, `assignedToId`, and ISO `dueDate`; sequence is preserved exactly.

The inventory requires an `assigned_to_id`, but the current backend has no authoritative mapping
from `employee`, `manager`, `hr`, or `it` owner types to a user. The implementation therefore
rejects task JSON without an explicit `assignedToId`; it does not invent task ownership. A future
owner-resolution contract is required before templates that specify only generic owner types can
be activated.

## Tests added

`OnboardingPersistenceRepositoryTests` covers tenant-scoped pending access lookup and ID
preservation, active onboarding-template eligibility/scope isolation, edited JSON instantiation,
sequence preservation, and malformed JSON rejection.

## Verification

- `dotnet build src\\ONEVO.Infrastructure\\ONEVO.Infrastructure.csproj --no-restore --verbosity minimal` passed once after the persistence implementation.
- `dotnet test tests\\ONEVO.Tests.Architecture\\ONEVO.Tests.Architecture.csproj --no-build --verbosity minimal` passed: 548 tests.
- `git diff --check` passed.
- A subsequent build, EF migration scaffolding, and focused tests were blocked because the local
  tooling attempted to reach `https://api.nuget.org/v3/index.json`, which this environment denies.
- `ONEVO.sln` is absent from this repository, so the requested solution build cannot run.
- Docker availability was not established; focused integration tests were skipped because the
  build/test precondition was blocked.

## Remaining finalization blockers

Do not implement finalization yet. It still needs the dedicated transactional issuance flow
described in the earlier reports, plus a supported owner-resolution policy for checklist owner
types and an explicit role-grant decision lifecycle.

## Exact next backend prompt

"Implement employee-onboarding finalization only as one transaction: validate the tenant-scoped
draft, template and edited task JSON; resolve checklist owner types through an explicit supported
owner-resolution policy; create employee/user/position assignment, employee onboarding tasks,
purpose-bound invitation token, approved non-sensitive access grants or pending
access_grant_requests, and durable outbox records. Never assign Owner/Admin roles and do not
invent workflow-engine behavior. Add focused RLS integration tests."
