# Task 12: Paginate Leave, Attendance and People lists

| | |
|---|---|
| Priority | 12 of 30 (lower number = build first) |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day |
| Area | Pagination, Performance |
| Depends on | 04 |

## What we found

These collection endpoints have no paging parameters:

- Leave: `LeaveApprovalsController` (`pending-approvals`, `all`, `approval-history`), `LeaveBalancesController` (`team`, `all`), `LeaveRequestsController` (`my`), `LeaveEntitlementsController`, `EmployeeLeavePoliciesController`, `LeaveHolidaysController`.
- Attendance: `AttendanceCorrectionsController` (`my`, `approvals`), `LocationChangeRequestsController` (`my`, `approvals`), `WorkAreaChangeRequestsController` (`my`, `approvals`).
- Core HR: `EmployeeChecklistTasksController`, `EmployeeOffboardingController` (list, `open-work-tasks`), `OffboardingBypassRequestsController`, `PeopleChecklistAssigneesController`.
- Shared: `NotificationsController` `GET`, `RolesController` `GET`.

`leave/balances/all` and `leave/approvals/all` grow with the number of employees times the number of leave types, so they will be the first to pass 200 ms on a real tenant.

## What to do

1. Use the standard from task 04. Notifications use the cursor form.
2. Move filters (status, date range, employee, legal entity) into the SQL query together with the paging.
3. Small fixed catalogues (leave types, work modes, clock-in policies) may stay unpaged. List them in the pull request so the decision is visible.
4. Keep an uncapped export path only where the product needs a full download, as a separate endpoint that streams a file.

## Done when

- None of the listed endpoints can return more than 100 rows in one response.
- Each changed query has a unit test for page 1, a middle page and an empty page.
- The frontend team has the list of changed endpoints.
