# Task 27: cross-tenant and object access probes (2026-11-19)

Branch: `claude/improve-task-27-monitoring-coverage-f5c0fe`, cut from `development` (846b92028) on top of the
first probe commit 5c26153f3. Nothing has been pushed.

Test class: `tests/ONEVO.Tests.Integration/Security/TenantAndObjectAccess/TenantAndObjectAccessTests.cs` with
`TenantAndObjectAccessFixture.cs`. It runs in the Testcontainers integration suite on SQL Server with row-level
security on: 94 tests, 94 passed after the fix. Two tenants are provisioned through the platform admin API and
signed in through the real login and session-exchange flow. Tenant A has its owner plus five users:

| User | Permissions | Coverage |
|---|---|---|
| Employee | none | none |
| Colleague | none | none (the victim of the same-tenant probes) |
| Other-team manager | `attendance:read`, `leave:approve`, `monitoring:read`, `monitoring:screenshots:request`, `employees:write` | none |
| HR | `leave:read`, `leave:manage`, `attendance:read` | none |
| Covering manager | `monitoring:read`, `monitoring:screenshots:request` | company-wide coverage of tenant A's legal entity (positive control) |

Every name written for tenant B or the colleague carries a marker string. Each probe checks that the status is
403 or 404, that a 404 has a body (an empty 404 is a routing miss and proves nothing), and that the marker is not
in the body. The statuses below come from the run on 2026-11-19 (TRX output of the test class).

## Finding: monitoring reads ignored management coverage (fixed)

Per-employee monitoring endpoints checked only the `monitoring:read` permission and never management coverage.
Any holder could read any employee in the tenant by changing `?employeeId=`, including signed screenshot URLs,
and with `monitoring:screenshots:request` could start a screen capture on any active device. The same data under
`/employees/{id}/overview/activity` was already gated by `[RequireVisibleEmployee]` and refused (403).

Fix:

- `RequireVisibleEmployeeAttribute` falls back to the query string when the route has no such value. A missing,
  repeated or unparseable id is refused (403), never treated as "no filter".
- `[RequireVisibleEmployee("employeeId")]` is on activity snapshots, daily summary, daily range, app-usage
  snapshots, daily report, daily report export, device-state snapshots and meeting signals.
- `GET /monitoring/screenshots`: with `employeeId`, the handler checks `IEmployeeViewAccess.CanViewAsync` (403).
  Without it, the list holds only the caller's viewable employees (new
  `IEmployeeViewAccess.ListViewableEmployeeIdsAsync`, backed by `IEmployeeRepository.ListVisibleIdsAsync`, the same
  predicate as `GetVisibleByIdAsync`). It was tenant-wide before.
- `GET /monitoring/screenshots/{id}/url`: 404, the same answer as a missing asset, when the caller cannot view
  the asset's employee. Older assets stored under the user id are checked through that user's employee.
- `POST /monitoring/screenshots/request`: the device's employee is checked before the toggle and consent checks,
  with the same 404 as a missing device, so the toggle's 403 no longer tells an outsider the device exists.
- `MonitoringEmployeeCoverageArchitectureTests` fails if a tenant monitoring action that takes a required
  `employeeId` query parameter lacks `[RequireVisibleEmployee("employeeId")]`.

| Probe | Request | Expected | Before fix | After fix | Fix |
|---|---|---|---|---|---|
| Monitoring: other-team manager GET activity daily range | `GET /api/v1/monitoring/activity/daily-range?employeeId={id}&from=2026-11-19&to=2026-11-19` | 403 or 404 | 200 | 403 | `[RequireVisibleEmployee("employeeId")]` |
| Monitoring: other-team manager GET activity daily summary | `GET /api/v1/monitoring/activity/daily-summary?employeeId={id}&date=2026-11-19` | 403 or 404 | 204 | 403 | `[RequireVisibleEmployee("employeeId")]` |
| Monitoring: other-team manager GET activity snapshots | `GET /api/v1/monitoring/activity/snapshots?employeeId={id}&date=2026-11-19` | 403 or 404 | 200 | 403 | `[RequireVisibleEmployee("employeeId")]` |
| Monitoring: other-team manager GET app-usage snapshots | `GET /api/v1/monitoring/app-usage/snapshots?employeeId={id}&date=2026-11-19` | 403 or 404 | 200 | 403 | `[RequireVisibleEmployee("employeeId")]` |
| Monitoring: other-team manager GET colleague screenshot url | `GET /api/v1/monitoring/screenshots/{id}/url` | 404 | 200 (signed URL) | 404 | `GetScreenshotUrlQueryHandler` checks `CanViewAsync` |
| Monitoring: other-team manager GET daily report | `GET /api/v1/monitoring/daily-report?employeeId={id}&date=2026-11-19` | 403 or 404 | 200 | 403 | `[RequireVisibleEmployee("employeeId")]` |
| Monitoring: other-team manager GET daily report export | `GET /api/v1/monitoring/daily-report/export?employeeId={id}&date=2026-11-19` | 403 or 404 | 200 (.xlsx) | 403 | `[RequireVisibleEmployee("employeeId")]` |
| Monitoring: other-team manager GET device-state snapshots | `GET /api/v1/monitoring/device-state/snapshots?employeeId={id}&date=2026-11-19` | 403 or 404 | 200 | 403 | `[RequireVisibleEmployee("employeeId")]` |
| Monitoring: other-team manager GET meeting signals | `GET /api/v1/monitoring/meetings/signals?employeeId={id}&date=2026-11-19` | 403 or 404 | 200 | 403 | `[RequireVisibleEmployee("employeeId")]` |
| Monitoring: other-team manager GET screenshots filtered to colleague | `GET /api/v1/monitoring/screenshots?employeeId={id}` | 403 or 404 | 200 | 403 | `GetScreenshotsQueryHandler` checks `CanViewAsync` |
| Monitoring: other-team manager GET screenshots without filter | `GET /api/v1/monitoring/screenshots` | 200, colleague left out | 200, colleague's screenshot listed | 200 | list limited to `ListViewableEmployeeIdsAsync` |
| Monitoring: other-team manager POST screenshot request | `POST /api/v1/monitoring/screenshots/request` | 404 | 403 (toggle check: device reached) | 404 | `RequestScreenshotCommandHandler` checks the device's employee first |

Positive control (`Monitoring_PositiveControls_CoveringManagerReadsTheColleague`): the covering manager gets 200
(204 for the daily summary, which has no content until the day is aggregated) on every endpoint above, sees the
colleague's screenshot in the unfiltered list, and is not refused with 404 on the screenshot request.

Behaviour changes to note:

- These endpoints now answer 403 instead of 400 when `employeeId` is missing.
- HR and owners see monitoring data only for employees inside their coverage, the same rule as the employee
  overview widgets. A role with `monitoring:read` but no coverage now sees nothing.
- A device whose user has no employee row yet (tray activated before onboarding) can no longer be sent a
  screenshot request: its owner cannot be resolved to a viewable employee, so the request fails closed with 404.
- The unfiltered screenshot list does not include legacy assets stored under a user id instead of an employee id.
  Those stay reachable through the daily report and the URL endpoint.

## Open product decision (not changed)

`GET /api/v1/monitoring/reports/productivity` still needs only `monitoring:read`. With `scope` and `scopeId` it
aggregates productivity across the tenant, a department or a team, whether or not the caller covers those people.
It returns aggregates, not one employee's raw data, so whether coverage should limit it is a product decision.
Options: limit the aggregate to the caller's coverage, or keep it tenant-wide and give it its own permission.

## Cross-tenant probes (tenant A's owner against tenant B's ids)

All refused. No tenant B data in any body.

| Probe | Request | Expected | Actual | Response detail |
|---|---|---|---|---|
| Attendance: GET day detail | `GET /api/v1/attendance/time-tracking/history-detail?employeeId={id}&date=2026-11-19` | 403 or 404 | 403 | Access denied. |
| Conversations: GET messages | `GET /api/v1/conversations/{id}/messages` | 403 or 404 | 404 | Conversation not found. |
| Conversations: POST message | `POST /api/v1/conversations/{id}/messages` | 403 or 404 | 404 | Conversation not found. |
| CoreHr: GET employee | `GET /api/v1/employees/{id}` | 403 or 404 | 404 | The employee or selected organization record could not be found. |
| Dashboard: GET available widgets | `GET /api/v1/dashboards/{id}/widgets/available` | 403 or 404 | 404 | Dashboard not found. |
| Dashboard: GET bootstrap | `GET /api/v1/dashboards/{id}/bootstrap` | 403 or 404 | 404 | Dashboard not found. |
| Dashboard: POST reset | `POST /api/v1/dashboards/{id}/reset` | 403 or 404 | 404 | Dashboard not found. |
| Leave: GET leave type | `GET /api/v1/leave/types/{id}` | 403 or 404 | 404 | Leave type not found. |
| Leave: GET request approval | `GET /api/v1/leave/requests/{id}/approval` | 403 or 404 | 404 | Leave request was not found. |
| Leave: GET request detail | `GET /api/v1/leave/requests/{id}/detail` | 403 or 404 | 404 | Leave request was not found. |
| Leave: POST approve request | `POST /api/v1/leave/requests/{id}/approve` | 403 or 404 | 404 | Leave request was not found. |
| Leave: POST cancel request | `POST /api/v1/leave/requests/{id}/cancel` | 403 or 404 | 404 | Leave request was not found. |
| Leave: PUT leave type | `PUT /api/v1/leave/types/{id}` | 403 or 404 | 404 | Leave type not found. |
| Org: DELETE department | `DELETE /api/v1/org/legal-entities/{id}/departments/{id}` | 403 or 404 | 404 | Legal entity not found. |
| Org: DELETE legal entity logo | `DELETE /api/v1/org/legal-entities/{id}/logo` | 403 or 404 | 404 | Company not found. |
| Org: GET department | `GET /api/v1/org/legal-entities/{id}/departments/{id}` | 403 or 404 | 404 | Legal entity not found. |
| Org: GET department under own legal entity | `GET /api/v1/org/legal-entities/{id}/departments/{id}` | 403 or 404 | 404 | Department not found. |
| Org: GET legal entity settings | `GET /api/v1/org/legal-entities/{id}/general-settings` | 403 or 404 | 404 | Company not found. |
| Org: PUT department | `PUT /api/v1/org/legal-entities/{id}/departments/{id}` | 403 or 404 | 404 | Legal entity not found. |
| Org: PUT own logo to tenant B's file | `PUT /api/v1/org/legal-entities/{id}/logo` | 403 or 404 | 404 | File not found. |
| Roles: GET role | `GET /api/v1/roles/{id}` | 403 or 404 | 404 | Role not found. |
| Roles: PATCH role | `PATCH /api/v1/roles/{id}` | 403 or 404 | 404 | Role not found. |
| Roles: PUT role permissions | `PUT /api/v1/roles/{id}/permissions` | 403 or 404 | 404 | Role not found. |
| Storage: DELETE file | `DELETE /api/v1/files/{id}` | 403 or 404 | 404 | File not found. |
| Storage: GET logo file | `GET /api/v1/files/{id}` | 403 or 404 | 404 | File not found. |
| Storage: GET pending file | `GET /api/v1/files/{id}` | 403 or 404 | 404 | File not found. |
| Storage: GET task file | `GET /api/v1/files/{id}` | 403 or 404 | 404 | File not found. |
| Support: GET ticket | `GET /api/v1/support/tickets/{id}` | 403 or 404 | 404 | Support ticket ... was not found. |
| Support: POST ticket comment | `POST /api/v1/support/tickets/{id}/comments` | 403 or 404 | 404 | Support ticket ... was not found. |
| Support: PUT ticket | `PUT /api/v1/support/tickets/{id}` | 403 or 404 | 404 | Support ticket ... was not found. |
| Work: DELETE project | `DELETE /api/v1/work/projects/{id}` | 403 or 404 | 404 | Project not found. |
| Work: DELETE task | `DELETE /api/v1/work/tasks/{id}` | 403 or 404 | 404 | Task not found. |
| Work: GET objective | `GET /api/v1/work/objectives/{id}` | 403 or 404 | 404 | Objective not found. |
| Work: GET project | `GET /api/v1/work/projects/{id}` | 403 or 404 | 404 | Project not found. |
| Work: GET project banner | `GET /api/v1/work/projects/{id}/banner` | 403 or 404 | 404 | Project not found. |
| Work: GET task | `GET /api/v1/work/tasks/{id}` | 403 or 404 | 404 | Task not found. |
| Work: GET task file | `GET /api/v1/work/tasks/files/{id}` | 403 or 404 | 404 | File not found. |
| Work: PATCH task | `PATCH /api/v1/work/tasks/{id}` | 403 or 404 | 404 | Task not found. |

## Same-tenant probes (colleague and other-team ids inside tenant A)

All refused. No colleague data in any body.

| Probe | Request | Expected | Actual | Response detail |
|---|---|---|---|---|
| Attendance: employee GET colleague day detail | `GET /api/v1/attendance/time-tracking/history-detail?employeeId={id}&date=2026-11-19` | 403 or 404 | 403 | Access denied. |
| Attendance: other-team manager GET day detail | `GET /api/v1/attendance/time-tracking/history-detail?employeeId={id}&date=2026-11-19` | 403 or 404 | 403 | Access denied. |
| Conversations: non-participant GET messages | `GET /api/v1/conversations/{id}/messages` | 403 or 404 | 404 | Conversation not found. |
| CoreHr: GET employee detail | `GET /api/v1/employees/{id}/detail` | 403 or 404 | 404 | The employee or selected organization record could not be found. |
| CoreHr: GET employee documents | `GET /api/v1/employees/{id}/documents` | 403 or 404 | 404 | Employee not found. |
| CoreHr: GET employee time-off overview | `GET /api/v1/employees/{id}/overview/time-off` | 403 or 404 | 403 | You do not have access to this employee. |
| CoreHr: PUT employee job details | `PUT /api/v1/employees/{id}/job-details` | 403 or 404 | 404 | The employee could not be found. |
| CoreHr: employee GET colleague | `GET /api/v1/employees/{id}` | 403 or 404 | 403 | You do not have access to manage this employee. |
| CoreHr: employee GET colleague detail | `GET /api/v1/employees/{id}/detail` | 403 or 404 | 403 | You do not have access to manage this employee. |
| CoreHr: employee GET colleague documents | `GET /api/v1/employees/{id}/documents` | 403 or 404 | 403 | You do not have access to this employee's documents. |
| CoreHr: employee GET colleague time-off | `GET /api/v1/employees/{id}/overview/time-off` | 403 or 404 | 403 | You do not have access to this employee. |
| CoreHr: other-team manager GET activity overview | `GET /api/v1/employees/{id}/overview/activity` | 403 or 404 | 403 | You do not have access to this employee. |
| CoreHr: other-team manager GET attendance overview | `GET /api/v1/employees/{id}/overview/attendance` | 403 or 404 | 403 | You do not have access to this employee. |
| CoreHr: other-team manager GET detail | `GET /api/v1/employees/{id}/detail` | 403 or 404 | 403 | You do not have access to manage this employee. |
| CoreHr: other-team manager GET employee | `GET /api/v1/employees/{id}` | 403 or 404 | 403 | You do not have access to manage this employee. |
| CoreHr: other-team manager PUT job details | `PUT /api/v1/employees/{id}/job-details` | 403 or 404 | 403 | You do not have access to manage this employee. |
| Dashboard: employee GET colleague bootstrap | `GET /api/v1/dashboards/{id}/bootstrap` | 403 or 404 | 404 | Dashboard not found. |
| Dashboard: employee POST reset colleague dashboard | `POST /api/v1/dashboards/{id}/reset` | 403 or 404 | 404 | Dashboard not found. |
| Leave: employee GET colleague request detail | `GET /api/v1/leave/requests/{id}/detail` | 403 or 404 | 403 | Permission 'leave:read' required. |
| Leave: employee POST cancel colleague request | `POST /api/v1/leave/requests/{id}/cancel` | 403 or 404 | 403 | This request does not belong to you. |
| Leave: other-team manager GET approval | `GET /api/v1/leave/requests/{id}/approval` | 403 or 404 | 403 | This request is not assigned to you |
| Leave: other-team manager POST approve | `POST /api/v1/leave/requests/{id}/approve` | 403 or 404 | 403 | This request is not assigned to you |
| Roles: employee PATCH owner role | `PATCH /api/v1/roles/{id}` | 403 or 404 | 403 | Permission 'roles:manage' required. |
| Storage: HR GET colleague pending file | `GET /api/v1/files/{id}` | 403 or 404 | 404 | File not found. |
| Storage: employee DELETE colleague pending file | `DELETE /api/v1/files/{id}` | 403 or 404 | 403 | Only the uploader may delete this file. |
| Storage: employee GET colleague pending file | `GET /api/v1/files/{id}` | 403 or 404 | 404 | File not found. |
| Storage: non-member GET task file | `GET /api/v1/files/{id}` | 403 or 404 | 404 | File not found. |
| Work: non-member GET objective | `GET /api/v1/work/objectives/{id}` | 403 or 404 | 403 | You do not have access to this milestone. |
| Work: non-member GET project | `GET /api/v1/work/projects/{id}` | 403 or 404 | 403 | You do not have access to this project. |
| Work: non-member GET project banner | `GET /api/v1/work/projects/{id}/banner` | 403 or 404 | 403 | You do not have access to this project. |
| Work: non-member GET task | `GET /api/v1/work/tasks/{id}` | 403 or 404 | 404 | Task not found. |
| Work: non-member GET task file | `GET /api/v1/work/tasks/files/{id}` | 403 or 404 | 404 | File not found. |
| Work: non-member PATCH task | `PATCH /api/v1/work/tasks/{id}` | 403 or 404 | 403 | Only members of this module can change its tasks. |

## Other checks in the class (all pass)

| Test | What it proves |
|---|---|
| `PositiveControls_RightfulCallerGetsTheObject` | the probed endpoints return 200 to the rightful user, so a refusal is not just a broken route |
| `WritesFromOtherTenant_LeftTenantBObjectsUnchanged` | the cross-tenant writes changed nothing in tenant B |
| `CreateTask_WithAnotherUsersPendingUpload_DoesNotLinkIt` | a task cannot attach another tenant's or a colleague's pending upload by id |
| `LinkLogo_WithAnotherTenantsPendingLogo_IsRefused` | a logo cannot be set from another tenant's file |
| `ListFilters_WithSomeoneElsesId_DoNotWidenTheResult` | seven list endpoints given another employee's or tenant's id in a filter return none of that data |
| `TenantASession_OnTenantBHost_IsUnauthenticated` | a session cookie is bound to its tenant host |
| `InboxHub_NegotiateIsBoundToTheSessionsOwnTenantHost` | the inbox hub refuses a session on another tenant's host |
| `AgentCommandsHub_RefusesABrowserSession` | the agent-commands hub refuses a browser session (401 or 403) |
| `Hubs_ExposeNoClientInvokableMethods` | neither hub has a public method a client could invoke, so groups come only from the connection's claims |

## Minor observations (not leaks)

- Inside a tenant, a colleague's employee id returns 403 while an unknown id returns 404, so anyone can tell an id
  exists. The employee API is documented this way.
- Leave approve or reject: the policy check runs before the approver check. For a request with no policy, a
  non-approver gets 409 instead of 403. No data is returned and nothing changes.
- `employees:read` is a retired permission code, so tests must not seed it.
