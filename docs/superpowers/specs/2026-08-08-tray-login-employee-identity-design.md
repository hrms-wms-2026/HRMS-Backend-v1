# Tray Login: Real Employee Identity — Design

**Date:** 2026-08-08
**Status:** Approved
**Repos affected:** `HRMS-Backend-v1` (backend), `tray_app_maui` (Agent Service + TrayApp)

## Context

The ONEVO WorkPulse Agent activation/login flow (paste 8-char activation code → device JWT + refresh
token) is already fully implemented and integration-tested end-to-end across the backend
(`Features/Monitoring/TrayActivation`), the Agent Service (`AgentWorker.HandleActivationCodeSubmitAsync`,
`OnevoApiClient`, `CredentialStore`, `TokenRefreshService`), and the TrayApp
(`ConnectWorkspaceViewModel`, `NamedPipeClient.SendActivationAsync`).

The one real functional gap in that pipeline: after a successful login, the TrayApp has no source of
truth for *who* just logged in. `PrepareWorkspaceViewModel.LoadAsync` hardcodes
`EmployeeFullName = "Pirakeerthan"`, `EmployeeEmail = "pirakeerthan@onexso.com"`,
`EmployeeId = "ONEXSO1234"` for every device, regardless of which employee actually activated it.
This same fake string then flows into `Preferences` and is read back by `ClockInViewModel` and
`ReviewSetupViewModel`.

Root cause: `TrayAuthResponseDto` (returned by `/exchange` and `/refresh`) carries only token fields —
no employee name/email/number. The `EnrollmentResultPayload` IPC contract already has an `EmployeeName`
field and `ConnectWorkspaceViewModel` already has dead code to consume it — the plumbing was designed
for this but never completed on the backend.

## Decisions (confirmed with user)

1. **Scope**: employee identity fix only. Rate limiting on `/exchange`/`/refresh`, the named-pipe ACL
   fallback hardening, and the stale architecture-doc §9/§10 rewrite are explicitly out of scope —
   tracked as separate follow-ups, not part of this plan.
2. **Delivery mechanism**: embed `employee_name` / `employee_email` / `employee_number` directly in the
   existing `/exchange` and `/refresh` response bodies (extends `TrayAuthResponseDto`), not a new
   separate "me" endpoint. Rationale: no extra round trip, completes plumbing that already half-exists
   in the IPC contract and `ConnectWorkspaceViewModel`. Note: only `/exchange` (activation time) actually
   reaches the TrayApp's cached display — see the Service section below for why `/refresh` carrying the
   same fields doesn't translate into a live self-healing cache today.

## Design

### 1. Backend (`HRMS-Backend-v1`, `Features/Monitoring/TrayActivation`)

- **`TrayAuthResponseDto`**: add three nullable string fields —
  `employee_name`, `employee_email`, `employee_number`.
- **`ITrayActivationRepository`** (existing interface, already injected into both handlers): add
  `FindEmployeeProfileAsync(Guid userId, Guid tenantId, CancellationToken ct)` returning a small
  projection (`FirstName`, `LastName`, `Email`, `EmployeeNumber`) from the existing `Employees` table
  (`Domain/Features/CoreHr/Employee/Entities/Employee.cs`, joined on `UserId` + `TenantId`).
  Implemented in `EfTrayActivationRepository`.
- **`ExchangeActivationCodeCommandHandler`**: after creating the `TrayDeviceRegistration` (UserId/TenantId
  already known), call the new lookup and populate the 3 new DTO fields.
  - Fallback: if no `Employee` row exists for that `UserId` (HR profile not yet linked), use the `User`
    entity's `FirstName`/`LastName`/`Email` (already loaded in this handler) for name/email, and leave
    `employee_number` as `null`. Do not fail the exchange — login must still succeed.
- **`RefreshTrayTokenCommandHandler`**: same lookup + same fallback, so every 45-min refresh keeps the
  cached identity current.
- No new endpoint, no schema change, no migration. `TrayDeviceScheme` bearer-token exception to the
  "no JWT for browser sessions" backend rule is pre-existing and already covered by the current test
  suite — not something this plan introduces or changes.

### 2. Agent Service (`ONEVO.Agent.Service`)

- **`OnevoApiClient.TrayAuthPayload`**: add `EmployeeName`, `EmployeeEmail`, `EmployeeNumber`
  (`[JsonPropertyName]` matching the new snake_case fields).
- **`ONEVO.Agent.Shared` `EnrollmentResultPayload`**: add `EmployeeEmail`, `EmployeeNumber` alongside the
  existing `EmployeeName`.
- **`AgentWorker.HandleActivationCodeSubmitAsync`**: replace the hardcoded `employeeName: null` in the
  `ReplyEnrollmentAsync` call with `result.Auth.EmployeeName` / `EmployeeEmail` / `EmployeeNumber`.
- `/refresh` also gets the 3 new fields for consistency (same response shape as `/exchange`), but the
  **Service does not act on them** — `TokenRefreshService` runs in the Windows Service process, which
  has no IPC channel to push unsolicited profile updates to the TrayApp (the only Service→Tray messages
  today are replies to a Tray-initiated request, or `EnrollmentResult`/`LogoutResult`/`StatusResponse`
  pushed right after a Tray action). Building that push channel is out of scope here. Practical effect:
  the cached employee identity is set once, at activation time, and only changes again on the next
  re-activation — acceptable since employee name/email/number changes are rare and this matches how the
  rest of the onboarding flow already works.
- The JWT itself is untouched — these are plain display fields, never stored in `CredentialStore`
  (which remains DPAPI-protected and JWT-only). Consistent with "Service owns credentials, TrayApp never
  stores the JWT."

### 3. TrayApp (`ONEVO.Agent.TrayApp`)

- **`ConnectWorkspaceViewModel.VerifyAndConnectAsync`**: the existing
  `if (!string.IsNullOrWhiteSpace(result.EmployeeName)) Preferences.Set("onevo.employee_display_name", ...)`
  block becomes live (backend now actually returns a value). Add the same pattern for
  `onevo.employee_email` and `onevo.employee_id`.
- **`PrepareWorkspaceViewModel.LoadAsync`**: remove the 3 hardcoded strings; read
  `EmployeeFullName` / `EmployeeEmail` / `EmployeeId` from `Preferences` using the same keys
  `ReviewSetupViewModel` and `ClockInViewModel` already read. Keep the existing staged
  `Task.Delay` progress animation — it's onboarding UX pacing only; the data is already available
  synchronously by the time this screen loads, no real fetch is needed here.

### 4. Testing

- Extend the happy-path assertions in `TrayActivationIntegrationTests.cs` (`Exchange_ValidCode_...`,
  `Refresh_...`) to check the 3 new response fields.
- New integration test: exchange succeeds when the activating `User` has no linked `Employee` row —
  response falls back to `User` name/email, `employee_number` is null, 200 status (not a failure).
- Unit test for the new repository fallback logic if it contains branching beyond a simple query.
- Existing 16 `TrayActivationIntegrationTests` must continue passing unmodified otherwise.

## Out of scope (explicitly deferred)

- Rate limiting on `/exchange` and `/refresh`.
- Named-pipe ACL creation-failure fallback hardening (`NamedPipeServer.CreateSecurePipe`).
- Updating the stale `ONEVO_Agent_Architecture_Flow_Folder_Structure.md` §9/§10 (still describes the old
  browser device-code flow instead of the actual paste-code flow) and the matching stale doc-comment in
  `OnevoApiClient.cs`.
- `"EXPIRED"` / `"ALREADY_ENROLLED"` distinct `EnrollmentResultPayload` error codes (currently both
  collapse into the generic invalid-code path — functionally fine today).
