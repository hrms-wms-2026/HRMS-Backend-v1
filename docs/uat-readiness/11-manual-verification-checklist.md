# 11. Manual Verification Checklist

## Table of Contents

1. [Release Candidate Gates](#1-release-candidate-gates)
2. [Environment and Security](#2-environment-and-security)
3. [Role Journeys](#3-role-journeys)
   3.1 [Tenant Owner](#31-tenant-owner)
   3.2 [HR Manager](#32-hr-manager)
   3.3 [Manager](#33-manager)
   3.4 [Employee](#34-employee)
4. [Tenant Isolation](#4-tenant-isolation)
5. [Tray App](#5-tray-app)
6. [Already Verified in This Audit](#6-already-verified-in-this-audit)

Mark each item **Pass**, **Fail (ticket)** or **N/A**, and add the tester and date.

## 1. Release Candidate Gates

- [ ] Backend CI is green on the UAT commit, including the full integration suite (UAT-004). Record the link.
- [ ] Frontend lint, zero-warnings build and unit tests are green on the UAT commit (UAT-002, UAT-013).
- [ ] Backend and frontend commits are recorded together. Planner scope is decided (UAT-001).
- [ ] Database backup taken. Migrations rehearsed on a copy (UAT-020).
- [ ] Security scanners re-run on the release candidate (`security-scan.ps1` for both repositories).

## 2. Environment and Security

- [ ] The API log shows the environment is **not** `Development`, and no `Dapi*`/`DevSmokeTest` seeder ran (UAT-003).
- [ ] `/swagger` is not reachable.
- [ ] CORS preflight from `https://{tenant}.{uat-domain}` succeeds; `https://evil.example` fails.
- [ ] `/health/ready` reflects the database state (UAT-008).
- [ ] Session cookie is `HttpOnly`, `Secure` and has the expected `SameSite`; the CSRF header is required on POST.
- [ ] Login brute-force behaviour (lockout or rate limit) is acceptable.

## 3. Role Journeys

### 3.1 Tenant Owner

- [ ] Log in. The dashboard widgets load (Weekly Work Progress, Action Center, Active Projects, Schedule).
- [ ] Create a legal entity, department and position. Add management coverage. The coverage modal shows existing owners (UAT-006).
- [ ] Invite an employee. Resend and revoke the invitation.
- [ ] Create roles, assign permissions, check that a user sees only the allowed menus.
- [ ] Configure clock-in policy, leave types and policies, and notification settings.
- [ ] Customize the dashboard layout, then reset it.

### 3.2 HR Manager

- [ ] Onboard an employee end to end (draft, documents, avatar, checklist). Bulk onboarding with a CSV.
- [ ] Offboard an employee: checklist tasks and approvals.
- [ ] Reveal bank details. The reveal is audited.
- [ ] Adjust leave entitlements; the balance audit shows the change.

### 3.3 Manager

- [ ] My Team shows only covered employees. Opening a non-covered employee gives 403.
- [ ] Approve and reject leave, attendance corrections and work area change requests from the Inbox.
- [ ] Work management: create a project, module and sprint; start and complete the sprint; approve a task change.
- [ ] Meetings: create, edit and cancel; invitees see the meeting; non-invitees do not.

### 3.4 Employee

- [ ] First login shows legal consent, then the dashboard.
- [ ] Clock in, start and end a break, clock out. Times show in the right time zone (UAT-015).
- [ ] Request leave with a document; cancel it.
- [ ] Request an attendance correction.
- [ ] Update personal information, emergency contacts and dependents.
- [ ] Inbox chat: direct and group chats, attachments, reactions, edit and delete own message.
- [ ] Enable MFA, log out, log in with MFA.
- [ ] Keyboard-only pass over the dashboard, planner or calendar, and the leave request form.

## 4. Tenant Isolation

- [ ] With tenant A's session, tenant A's IDs work, and tenant B's IDs return 404 on employees, projects, leave requests and files.
- [ ] Tenant A's session on tenant B's host returns 401.
- [ ] File download URLs from tenant A do not work in tenant B.

## 5. Tray App

- [ ] Generate an activation code on the web and activate the tray app.
- [ ] Clock in and out from the tray. The web attendance view updates.
- [ ] Face check-in: enrolment, match and retry. HR alert fires on repeated failures.
- [ ] Screenshots and activity snapshots arrive. The monitoring policy is applied.
- [ ] Tray notifications are delivered and acknowledged.
- [ ] Revoke the device. Tray calls then fail with 401.

## 6. Already Verified in This Audit

| Item | Result |
|---|---|
| Fresh database migration with all 268 migrations | Pass |
| Owner walk across 21 routes | Pass, apart from the planner 404s (UAT-001) |
| Employee permission probes (roles, users, org, other employee's detail and documents) | 403 as expected |
| Cross-tenant probes (IDs and host) | 404 and 401 as expected |
| Legal consent on first employee login | Pass |
