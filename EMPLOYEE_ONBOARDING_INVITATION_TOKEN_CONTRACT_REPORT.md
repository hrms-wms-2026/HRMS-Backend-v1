# Employee onboarding invitation-token contract

## Changes

`InvitationToken` now has an explicit `Purpose` (employee onboarding uses
`employee_onboarding`) and optional legal-entity, employee, and onboarding-draft linkage.
The migration defaults historical tokens to `general`, so existing invite records are not
silently classified as employee invitations.

The existing general password and Google acceptance handlers explicitly reject employee
onboarding tokens. This prevents an employee token from accidentally inheriting the existing
position default-role assignment behavior, including any unsuitable role.

The dedicated employee email outbox payload added previously already carries employee token,
tenant, legal entity, employee, name, expiry, and raw token. It remains asynchronous through
the email abstraction.

## Remaining blocker

There is no implemented employee acceptance command yet. Safely implementing it requires the
finalization transaction to create the inactive user and employee first, issue this purpose
token, and provide a repository method to retrieve the linked employee. It must then activate
only that user, mark the token used, and avoid default position-role assignment unless a
separate supported employee-role policy is defined. No owner/admin role is assigned here.

Checklist/task schema remains absent. Finalization is therefore not yet safe to implement.

## Verification

The focused build/test verification is pending after this token schema change. No frontend was
modified and no commit was created.
