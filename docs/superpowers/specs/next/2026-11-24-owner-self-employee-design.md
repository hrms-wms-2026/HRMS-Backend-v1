# Owner without an employee profile: calm home screen and "Add yourself as an employee"

Date: 2026-11-24. Status: approved by the user in conversation. Repos: HRMS-Backend-v1 (backend), Hrms--Web-application---front-end---v1 (web).

## Why

A company owner signs in through the owner invitation. That creates a **login** (`users` row) but no **employee profile** (`employees` row). The web home screen is all "my work" (weekly progress, approvals, projects, today's schedule). The backend answers those requests with "No employee record for the current user." That message appears in 114 places, as 403 or 404. So for the owner:

- every widget shows "Couldn't load", which looks broken although nothing is broken;
- the error toast shows the raw "No employee record for the current user." message.

Most owners also work in the company, so they need a way to become an employee. A brand-new company has no legal entity, department or position yet, so this can't happen at first sign-in.

## Goals

1. An account with no employee profile sees a calm home screen: no failing requests, no red toast, and a clear message saying the employee profile isn't set up yet.
2. Once the company has what an employee needs, the owner can **add themselves** through the existing Add Employee wizard, with their name and email filled in and the email locked.
3. Finishing that wizard for yourself creates the employee on **your existing login**, makes the position seat **active immediately**, and sends **no invitation email**.
4. The home screen switches to normal without signing out.

## Non-goals and anti-goals

- No change for any other employee: finalizing someone else works exactly as today (planned seat, invitation email, seat activated on acceptance).
- Owners are not forced to add themselves; the step is optional.
- No new fields in the wizard. All of its current validation stays: legal entity, position with a department, work mode, employment type, start date, seat capacity, unique employee number.
- **Anti-goal:** "add yourself" must never let someone attach an employee to *another* person's login, or skip the invitation for anyone but themselves.

## What exists today (facts the design relies on)

- **Finalize** (`OnboardingDraftWriteService` finalize path):
  - It already does found-or-built on the login: `GetByTenantAndEmailAsync(tenant, normalizedEmail)` reuses an existing `users` row.
  - It reserves the seat as **planned** (`TryReservePositionAssignmentAsync`).
  - It assigns the position access template role, when the template is active and doesn't require approval, or when the actor self-authorizes.
  - It creates an `InvitationToken`, purpose `employee_onboarding`, and enqueues `EmployeeOnboardingInviteEmail`.
- **`AcceptEmployeeInvitation`:**
  - It handles a returning user (`IsActive` and a password set) without asking for a new password.
  - It activates the planned seat (`ActivatePlannedAsync`).
- **`GET /auth/me`** (`GetCurrentSessionQueryHandler`) reads the employee from the database on every call (`GetByUserIdAsync`). A fresh session check after finalize therefore picks up the new `employeeId`; there's no token claim to re-issue.
- **Web:** `AuthStore.employeeId()` is `null` for an account without an employee profile.
- **Errors:** the "No employee record" message comes from a shared constant (`ConversationRules.NoEmployeeMessage`) in some places and from literal strings in others.

## Approaches considered

1. **Self-finalize in the existing wizard (chosen).** Same wizard and same draft. At finalize, if the draft's work email is the acting user's own login email, the user found is the actor. Then:
   - activate the seat directly instead of reserving it as planned;
   - create no invitation and send no email.
   The web adds an entry point that opens the wizard prefilled, and a calm home screen. This is the smallest change and reuses all of the wizard's validation.
2. **A separate "create my profile" endpoint and form.** It would duplicate the wizard's rules: position capacity, access template roles, checklist, monitoring overrides. Rejected: two places to keep in step.
3. **Auto-create the employee when the owner accepts the invite.** Rejected: at that moment the company has no legal entity or position, and the user chose a manual, optional step.

## Design

### Backend

- **Self-finalize rule.** In finalize, a draft is a *self* draft when its normalized work email equals the acting user's normalized login email (`ICurrentUser`). For a self draft:
  - The found login must be the acting user (`user.Id == actingUserId`). Anything else is rejected, which covers the anti-goal.
  - Reserve the seat and **activate it in the same transaction**: reuse `ActivatePlannedAsync` right after the reservation, or an equivalent single call.
  - Assign roles the same way as today (access template rules unchanged).
  - **Skip** creating the `InvitationToken` and enqueueing `EmployeeOnboardingInviteEmail`.
  - The response says the employee was created and is active, so the web knows not to show "invitation sent".
  - Monitoring overrides, checklist tasks and the office project: same as today.
- **Permission.** Unchanged: finalize already requires the onboarding permission, which the owner has. The self rule only removes the invitation; it doesn't widen who can finalize.
- **"Can I add myself?" signal.** The web needs to know when to show the button. Add a small endpoint, or a field on an existing onboarding lookup, that returns whether:
  - the acting user has no employee profile, and
  - the company has at least one active legal entity and at least one position with a free seat.
  The exact shape is the builder's call. Prefer extending an existing onboarding lookup over a new controller.

### Web

- **Home screen with no employee profile** (`AuthStore.employeeId()` is `null`):
  - The employee-scoped widgets do **not** call their APIs. That covers weekly progress, Action Center approvals, active projects and today's schedule; the builder confirms the full list on the dashboard.
  - They show one calm panel instead: "Your employee profile isn't set up yet." When the signal above says yes, the panel also offers **Add yourself as an employee**. When it says no, it says what's missing: "Add a legal entity and a position first", with a link to the org setup.
  - Widgets that don't need an employee profile keep working.
- **No raw toast.** The shared error handling must not toast the "No employee record for the current user." message. Any request that still slips through is handled quietly. Keep this narrow: match that message or a stable error code, not all 403s and 404s.
- **Entry point.** "Add yourself as an employee" opens the existing Add Employee wizard with first name, last name and work email prefilled from the session, and the work email locked. Everything else is the normal wizard.
- **After finishing for yourself:** call `checkSession()`. The employee id now resolves, the home screen reloads its widgets normally, and the "invitation sent" success copy is replaced by "You're set up as an employee".

## Edge cases

- **A position at capacity, or a missing work mode or employment type:** the existing validation messages apply unchanged.
- **The owner's email already has an employee row** (someone else added them earlier): the existing "An employee with this work email already exists" conflict applies. The button shouldn't show, because they have a profile.
- **An owner whose login email differs from the email typed in the wizard:** the email is locked to the login email, so this can't happen through the UI. The backend self rule still keys on the email match.
- **Someone else's email, finalized by the owner:** a normal draft. It reserves a planned seat and sends an invitation, unchanged.

## Testing

- **Backend unit tests:**
  - a self finalize activates the seat, creates no invitation and sends no email;
  - a draft for another person behaves exactly as before;
  - a self draft whose found user isn't the actor is rejected;
  - the "can I add myself" signal covers each case: has a profile; no legal entity; no free seat; ready.
- **Web specs:**
  - with no employee profile, the home widgets make no API calls and show the panel;
  - the button shows only when the signal is true;
  - the toast is suppressed for the no-employee message;
  - the wizard is prefilled and the email locked;
  - after a self finalize, the store re-checks the session.
- **Live check on App Service:** an owner signs in to a calm home screen, sets up the org, adds themselves, and the home screen loads normally.

## Left to the builder

- The exact shape and name of the "can I add myself" signal.
- The exact list of employee-scoped home widgets.
- How the toast suppression matches: the message constant, or an error code if one exists.
- The panel's styling, using the existing empty-state components.
