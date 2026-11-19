# Task 08: Paginate Work Management lists

| | |
|---|---|
| Priority | 8 of 30 (lower number = build first) |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day |
| Area | Pagination, Performance |
| Depends on | 04 |

## What we found

Work Management has 73 GET endpoints and only 2 controllers accept paging parameters. These collection endpoints return everything:

- `ProjectsController`: `GET` (all projects), `GET mine`, `GET {id}/approval-history`
- `ObjectivesController`: `projects/{projectId}/objectives`, `objectives/mine`, `mine/history`, `invitations/mine`, `{id}/members`
- `SprintsController`: `sprints/{id}/tasks`, `sprints/{id}/activity`, `projects/{projectId}/sprints`, `objectives/{objectiveId}/sprints`
- `CommentsController`: `tasks/{taskId}/comments`
- `TasksController`: `GET me`
- `ProjectMonitorController`: `projects/{projectId}/monitor/alerts`
- `MeetingsController`: `GET`, action items, attendance
- `TaskDraftsController`, `ProjectCategoriesController`

## What to do

1. For each endpoint decide: page-based, cursor-based, or small bounded list that may stay as it is. Write the decision in a table at the top of the pull request.
2. Apply the shared `PageRequest` and `PagedResult` from task 04. Activity and comments use the cursor form.
3. Push the paging into the SQL query. Never load all rows and then page in memory.
4. For tree and graph endpoints (`{id}/tree`, `{id}/work-graph`, planner ranges) do not page. Instead cap by date range or depth and return only the fields the screen uses.
5. Keep the old behaviour for one release when no paging parameters are sent, but cap it at the maximum page size and log a warning, so the frontend can move over safely.

## Done when

- No Work Management endpoint can return more than 100 rows in one response, except the documented tree and graph endpoints.
- Unit tests cover paging for each changed query.
- The frontend team has the list of changed endpoints.
