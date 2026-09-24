# Employee onboarding foundation: blockers and contracts

## Outcome

Employee onboarding finalization is **not yet safe to implement**. This change adds the
authoritative tenant seat-policy contract and a dedicated employee-invitation email outbox
contract, but deliberately does not create an employee, user, role, or token from a draft.

## Contracts added

`TenantSubscription.IncludedSeats` and `TenantSubscription.OverageAllowed` are nullable,
tenant-wide billing fields. Null is intentional: it distinguishes unavailable billing data
from a zero-seat policy. No company-size, legal-entity, position, or frontend value is used.

`SeatEntitlementService` counts employees by tenant and returns:

- `Approved` when included capacity remains, or the explicit tenant policy permits overage.
- `Blocked` when capacity is exhausted and overage is explicitly disabled.
- `Undetermined` only when the subscription or either required policy value is absent or
  invalid.

The current model has no seat-reservation lifecycle. `WaitingForSeat` drafts are therefore
not counted as reservations; treating them as reserved would double-book capacity.

`employee_onboarding_invite_email` is a dedicated outbox type and payload. It carries tenant,
legal entity, employee, invitation-token, email, first/last name, token, and expiry. Its
handler uses `IEmailService`; it does not send mail in a request handler and carries no role
or permission assignment. It is separate from tenant-owner provisioning.

## Remaining blockers

The existing `InvitationToken` table has no purpose/type or employee/draft/legal-entity
association. Before a finalization handler queues the new payload, a migration must add an
employee-onboarding purpose and the required linkage, then a dedicated issuance service must
create an inactive/pending user using the normal auth repository and issue the token with
`IDateTimeProvider`. It must not assign Owner/Admin roles.

Position access templates exist, but the inspected model does not establish a safe employee
role-assignment lifecycle. Role mapping must be explicitly defined and validated against
tenant-scoped supported roles; otherwise it must wait for invitation acceptance.

No onboarding checklist/task schema was found. Checklist activation must remain unimplemented
until a tenant-scoped template/task model and ownership rules exist.

## Files changed

- `src/ONEVO.Domain/Features/SharedPlatform/Subscription/Entities/TenantSubscription.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Subscription/TenantSubscriptionConfiguration.cs`
- `src/ONEVO.Infrastructure/Services/CoreHr/SeatEntitlement/SeatEntitlementService.cs`
- `src/ONEVO.Application/Common/ServiceInterfaces/ISeatEntitlementService.cs` (existing contract consumed)
- `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs`
- `src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs`
- `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/OutboxHandlers/EmployeeOnboardingInviteEmailOutboxHandler.cs`
- `src/ONEVO.Application/DependencyInjection.cs`
- `src/ONEVO.Infrastructure/ExternalServices/Email/TransactionalEmailService.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/SeatEntitlement/SeatEntitlementServiceTests.cs`
- `tests/ONEVO.Tests.Integration/E2E/CapturingEmailService.cs`

Migration added: `20260810154000_AddTenantSeatPolicyContract`.

## Verification

`dotnet build src\\ONEVO.Api\\ONEVO.Api.csproj --no-restore --verbosity minimal` passed using
an isolated output directory because the normal API build output is locked by a running local
process. It has one pre-existing nullable warning in `AdminAuthController.cs:62`.

Focused and architecture tests remain to be run after the invitation-token/user contract is
implemented. `ONEVO.sln` does not exist at the repository root, so the requested solution
build cannot run.

## Next backend prompt

"Implement the employee onboarding finalization foundation: add a tenant-scoped
`InvitationToken` purpose and employee/draft/legal-entity linkage migration; build a dedicated
employee invitation issuance service using `IDateTimeProvider`, inactive/pending user creation,
hashed tokens and transactional outbox; define only supported position-template role mapping;
then add finalization only with focused unit/integration tests. Do not assign Owner/Admin roles
or create checklist tasks without a schema."
