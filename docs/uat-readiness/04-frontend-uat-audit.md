# 4. Frontend UAT Audit

## Table of Contents

1. [Scope and Method](#1-scope-and-method)
2. [Build, Lint and Test Results](#2-build-lint-and-test-results)
3. [Area Findings](#3-area-findings)
   3.1 [Authentication Flows](#31-authentication-flows)
   3.2 [Role and Permission Access](#32-role-and-permission-access)
   3.3 [Screens Walked at Runtime](#33-screens-walked-at-runtime)
   3.4 [API Integration and Error Handling](#34-api-integration-and-error-handling)
   3.5 [Security](#35-security)
   3.6 [Deployment Readiness](#36-deployment-readiness)
4. [Frontend Findings Summary](#4-frontend-findings-summary)

## 1. Scope and Method

- Repository `Hrms--Web-application---front-end---v1`, branch `Development` @ `1c6211b0`.
- Ran `npm ci`, `npm run lint`, `ng build` (production), `ng test --watch=false` and `npm audit --omit=dev`.
- Ran `ng serve` against the local backend. Logged in as the `dapi` tenant owner and walked 21 routes in Chromium, recording every API response with status 400 or higher and every console error. A plain employee account was used for permission checks.
- Static review of route guards, environment files and every HTTP call site (report 7).

## 2. Build, Lint and Test Results

| Check | Result | Evidence |
|---|---|---|
| `npm ci` | Pass | |
| `npm run lint` | **Fail: 5 errors** (the repository rule is 0 errors, 0 warnings) | `calendar-page.component.html:108,109`: two clickable elements without key handlers or focus (4 errors), from commit `7c09b204` (2026-10-27). `meeting-form-modal.component.ts:18`: unused import `MeetingCreatedResponse`, from commit `97463f51` (2026-10-26). |
| `ng build` (production) | **Pass, 0 warnings** | Zero-warnings gate holds. |
| `ng test --watch=false` | **5,067 pass, 6 fail in 1 file** under full-suite load | All 6 failures are in `landing-header.component.spec.ts` (`ReferenceError: requestAnimationFrame is not defined`). The file passes 17/17 on its own, so the test is **flaky** (UAT-014). |
| `npm audit --omit=dev` | **0 vulnerabilities** | |

## 3. Area Findings

### 3.1 Authentication Flows

- **Verified:** owner login lands on `/dashboard`.
- **Verified:** first employee login goes to `https://onexso.com:4200/auth/legal-consent` (root domain). After consent, the session exchange returns the user to `dapi.onexso.com/dashboard`.
- **Verified:** visiting tenant routes without a session redirects to `/auth/login?returnUrl=...`.
- **Untested:** MFA enrolment and challenge, password reset email links, invitation acceptance (they need email delivery). Covered in report 11.

### 3.2 Role and Permission Access

- Every authenticated route sits under `authGuard`. Feature areas use `permissionGuard` with `permissions` and `modules` data (`app.routes.ts:45-200`), for example `people` needs `MANAGES_PEOPLE`, `employees:write` or `employees:offboard`, and `settings/roles` needs `roles:read`.
- The frontend guards only hide screens. The real enforcement is on the backend, which was verified at runtime (report 3, section 3.2).

### 3.3 Screens Walked at Runtime

Owner session, 21 routes: `/dashboard`, `/dashboard/my-team`, `/work`, `/inbox`, `/people/employees`, `/people/offboarding`, `/people/checklists`, `/calendar`, `/planner`, `/time-off`, `/time-off/team-approvals`, `/time-off/policies`, `/attendance`, `/organization`, and `/settings/general`, `/roles`, `/users`, `/notifications`, `/bulk-onboarding`, `/clock-in-policy`.

| Observation | Result |
|---|---|
| Pages rendered without uncaught exceptions | All 21 |
| API errors | Only `GET /planner/me` 503 (planner off; this is how the app checks whether the planner is on) and the planner redesign 404s (UAT-001) |
| Weekly Work Progress card | Renders with data (backend endpoint now on `development`) |
| Redirects | `/time-off/team-approvals` → `/inbox?source=leave&tab=requests`; `/attendance` → `/attendance/overview`; `/organization` → `/organization/departments` (by design) |

### 3.4 API Integration and Error Handling

- 460 HTTP call sites scanned. Three call missing backend routes (UAT-001, UAT-006, UAT-017). Details are in report 7.
- `PlannerAvailabilityService` decides whether the planner is on with one `GET /planner/me` per session and hides the Planner nav item on 503. This works. A full page reload repeats the probe (about 4 calls per load from different stores). Info only.
- A mistyped work URL (`/work/approvals`, where `approvals` is read as a project id) shows two stacked "The requested record could not be found" panels instead of one not-found state. Info only.
- **Suspected (P3, UAT-015):** the dashboard mixes browser time and tenant time.

### 3.5 Security

Reused from the hardening report and still true on HEAD: no `bypassSecurityTrust*`; DOMPurify on editor `innerHTML`; no tokens in `localStorage`; CSRF header from the `onevo_csrf` cookie. `npm audit --omit=dev` reports 0 vulnerabilities.

### 3.6 Deployment Readiness

- `environment.staging.ts` and `environment.prod.ts` build the API URL from `window.location`, so the API must be served at `https://{tenant}.{root}:{port}` the same way as locally. Confirm this matches the UAT topology (UAT-003).
- **There is no CI pipeline** (UAT-002). The lint regression above reached `Development` unnoticed because only the local pre-push hook checks anything.
- Map tiles load from `*.tile.openstreetmap.org` (seen in the network log). UAT networks must allow this host, or location screens show empty maps.

## 4. Frontend Findings Summary

| ID | Severity | Title |
|---|---|---|
| UAT-001 | P1 | Calendar or planner calls `/planner/me/range` and `/me/focus`, which the backend `development` does not have |
| UAT-002 | P1 | No frontend CI; lint gate already broken on `Development` |
| UAT-013 | P2 | 5 ESLint errors on `Development` |
| UAT-006 | P2 | Position coverage modal: target occupancy request returns 404 and is silently empty |
| UAT-014 | P3 | Flaky `landing-header` spec under load |
| UAT-015 | P3 | Dashboard time-zone inconsistency (suspected) |
| UAT-017 | P3 | Dead `setTrayAppAccess()` call to a missing endpoint |
