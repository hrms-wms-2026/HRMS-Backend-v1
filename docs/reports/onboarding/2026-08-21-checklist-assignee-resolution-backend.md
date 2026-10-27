# Checklist assignee resolution — backend

Date: 2026-08-20  
Branch: `local/reporting-manager-run`  
No commit or push.

## Verified contract

`employee_checklist_tasks.assigned_to_id` is a **UserId** (FK `fk_employee_checklist_tasks_users_assigned_to_id`).

Existing `GET .../positions/{id}/active-holders` returns **EmployeeId only** (`ActiveHolderViewModel`). It must not be used as checklist `assignedToId`.

## Files changed

Modified:

- `src/ONEVO.Api/Contracts/CoreHr/ChecklistTemplates/ChecklistTemplateTaskRequest.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Models/ChecklistTaskContract.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Services/ChecklistTemplateTaskInputResolver.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Services/ChecklistTemplateHandlerSupport.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/DTOs/Responses/ChecklistTemplateResponse.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/CreateChecklistTemplate/CreateChecklistTemplateCommandHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/UpdateChecklistTemplate/UpdateChecklistTemplateCommandHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs`
- `src/ONEVO.Application/Features/CoreHr/PositionAssignment/Models/PositionOccupancyPreview.cs`
- `src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs`
- focused unit tests listed below

Added:

- `src/ONEVO.Api/Controllers/Tenant/CoreHr/PeopleChecklistAssigneesController.cs`
- `src/ONEVO.Api/Contracts/CoreHr/People/ChecklistAssigneeViewModel.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListChecklistAssignees/*`
- `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/EditedOnboardingTasksValidator.cs`
- `tests/ONEVO.Tests.Architecture/PeopleChecklistAssigneesControllerArchitectureTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ListChecklistAssigneesQueryHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDraft/EditedOnboardingTasksValidatorTests.cs`

## Endpoint / payload contract

### Assignee lookup (onboarding picker)

`GET /api/v1/people/checklist-assignees?legalEntityId={guid}&positionId={guid}`

Permission: `employees:write`  
Auth: `TenantPolicy`  
Tenant id: server-derived from `ICurrentUser` (never from the request body).

Legal entity and position must belong to the current tenant (`GetByIdForTenantAsync` + `GetByIdForLegalEntityAsync`). Otherwise 404.

Response:

```json
[
  {
    "employeeId": "guid",
    "userId": "guid",
    "displayName": "Jane Smith",
    "workEmail": "jane@company.com",
    "avatarFileId": "guid-or-null"
  }
]
```

`userId` is the value that must be sent as `assignedToId` on edited onboarding tasks.

Query: `AsNoTracking`, active `PrimaryEmployment` assignment, `EmploymentStatusIds.Active`, `UserId != Guid.Empty`. Inactive employees and users without an employee row are excluded.

### Template create/update task (reusable rule)

Employee (new hire):

```json
{ "ownerType": "employee", "assignedToId": null, "assigneePositionId": null, "dueOffsetDays": 0, "isRequired": true, "title": "..." }
```

Another person by position (no concrete person required):

```json
{ "ownerType": "custom_user", "assignedToId": null, "assigneePositionId": "position-guid", "dueOffsetDays": 0, "isRequired": true, "title": "..." }
```

Template GET now returns `assigneePositionId` on each task. Template JSON stores position; it is not resolved to a user at save time.

`assignedToId` remains accepted on templates for backward compatibility and is mutually exclusive with `assigneePositionId`.

### Onboarding draft `editedTasksJson` (concrete plan)

Absolute-date mode.

Employee task: `ownerType=employee`, no `assignedToId`.

Another-person task: **requires** `assignedToId` = active employee **UserId**. Position alone is not enough. Invalid/missing assignee → **400**.

Instantiation never mutates `template.TasksJson`. Non-employee tasks without `assignedToId` cannot be instantiated (would not silently assign the new hire).

## Template vs onboarding assignment

| | Template | Add Employee draft |
|---|---|---|
| Employee | deferred; no ids | still deferred; no `assignedToId` |
| Another person | position is enough | HR must pick a seated active person; send `userId` |
| Mutates template | write path updates template row only | draft `editedTasksJson` only |

## Tests run

- `dotnet build src/ONEVO.Api/ONEVO.Api.csproj` succeeded when written to `artifacts/api-build` (default `src/ONEVO.Api/bin` was locked by a running `ONEVO.Api` process).
- Focused unit tests: **60 passed** (`ChecklistTaskJsonContract`, `ListChecklistAssignees`, `EditedOnboardingTasksValidator`, create/update template, `SaveOnboardingDraft`).
- Architecture: **22 passed** (`PeopleChecklistAssigneesControllerArchitectureTests` + `ChecklistTemplatesControllerArchitectureTests`).
- `git diff --check`: no whitespace errors.

## Skipped checks

- Default-output `dotnet build src/ONEVO.Api/ONEVO.Api.csproj` (file lock from running API).
- Integration tests for `GetChecklistAssigneesAsync` SQL filters (inactive / no primary assignment). Handler tests cover tenant/LE isolation and `userId` mapping; SQL filters live in the repository.
- Offboarding instantiation of position-only templates without `editedTasksJson` (will fail until a concrete user is supplied).

## Remaining risks

- Existing stored templates that already baked a resolved `assignedToId` have no `assigneePositionId`; HR must pick a position again in the wizard.
- `GetDefaultForUserAsync` is used to validate draft assignees (user → employee). Multi-employee users could theoretically map to a different default employee than the seated one; position check mitigates when `assigneePositionId` is sent.
- Local `artifacts/` build output from locked-bin workaround is untracked and should not be committed.
