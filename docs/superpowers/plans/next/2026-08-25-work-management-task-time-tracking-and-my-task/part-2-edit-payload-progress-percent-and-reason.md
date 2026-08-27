# Part 2: Extend EditTask + TaskEditRequest with ProgressPercent and Reason

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ProgressPercent` (manual up/down edit, per spec §5 — both directions allowed) and an optional
`Reason` to both the direct-edit path (`EditTaskCommand`) and the non-owner request path
(`CreateTaskEditRequestCommand`/`TaskEditRequest`), including validation. This Part only threads the fields
through — Part 3 and Part 4 make the handlers actually write `TaskEditLog`/`TaskPercentageLog` rows using
them.

**Spec:** `docs/superpowers/specs/next/2026-08-25-work-management-task-time-tracking-and-my-task-design.md`
(§3 `TaskEditRequestPayload` extension, §5 manual edit rules)

**Depends on:** Part 1 (this Part does not touch the 4 new tables directly, but Part 3/4 which consume
these fields do).

## Architecture & Conventions

- `EditTaskCommand`/`CreateTaskEditRequestCommand` are plain `sealed record`s implementing
  `IRequest<Result<...>>` — add fields as additional positional parameters, do not convert to a class or
  add a builder.
- Validators are `FluentValidation.AbstractValidator<T>` — one `RuleFor` per field, mirroring the exact
  style already in `EditTaskCommandValidator`/`CreateTaskEditRequestCommandValidator` (read both in full
  before editing — every new rule below must match their existing message-string convention:
  `.WithMessage("...")`, sentence case, ending in a period).
- `TaskEditRequestPayload` is a `sealed record` serialized to `TaskEditRequest.PayloadJson` via
  `System.Text.Json` — `ProgressPercent` goes here (it's a WorkTask field being changed, same category as
  `EstimatedHours`/`StoryPoints`). `Reason` is **not** a payload field — it's edit metadata, so it follows
  the same pattern as `TaskEditRequest.DecisionComment` (a top-level entity column, not inside the JSON
  blob).
- Contracts in `TaskContracts.cs` (API layer) mirror the command records field-for-field — every field you
  add to a command must have a matching field added to its `*Request` contract record, or the controller
  can't pass it through.

## Global Constraints

- `ProgressPercent`, where present, must be `0..100` inclusive.
- `Reason`, where present, is free text, max 1000 characters (matches this module's other free-text fields
  like `TaskEditRequest.DecisionComment` — check its own validator/config for the exact existing max length
  and match it rather than inventing a different limit).

---

### Task 1: Add `Reason` column to `TaskEditRequest` (existing table, small migration)

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditRequest.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskEditRequestConfiguration.cs`
- Migration: `dotnet ef migrations add AddTaskEditRequestReason`

**Interfaces:**
- Produces: `TaskEditRequest.Reason` (`string?`) — Part 4's handler reads this when writing `TaskEditLog`.

- [ ] **Step 1: Add the property**

In `TaskEditRequest.cs`, add alongside `DecisionComment`:

```csharp
    public string? Reason { get; set; }
```

- [ ] **Step 2: Configure it**

In `TaskEditRequestConfiguration.cs`, add alongside the existing `DecisionComment` mapping:

```csharp
        builder.Property(r => r.Reason).HasColumnType("text");
```

- [ ] **Step 3: Generate and inspect the migration**

Run: `dotnet ef migrations add AddTaskEditRequestReason --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

Expect a single `AddColumn` call (`reason`, nullable `text`) against `task_edit_requests` — **no RLS block
needed here**, this table already has RLS from when it was first created; adding a nullable column to an
existing RLS-covered table needs no policy change. If the generated migration includes anything else
(an unrelated model-drift diff), stop and investigate before proceeding — it means the model snapshot was
already out of sync before this Part started.

- [ ] **Step 4: Dry-run validate — do NOT apply**

Same dry-run approach as Part 1 Task 5 Step 6.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditRequest.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskEditRequestConfiguration.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(work): add optional Reason column to TaskEditRequest"
```

---

### Task 2: Extend `TaskEditRequestPayload` and `CreateTaskEditRequestCommand` with `ProgressPercent`; command and payload with `Reason`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskEditRequestPayload.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskEditRequest/CreateTaskEditRequestCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskEditRequest/CreateTaskEditRequestCommandValidator.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskEditRequest/CreateTaskEditRequestCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskEditRequestCommandValidatorTests.cs`
  (create if it doesn't already exist; if it does, add to it)

**Interfaces:**
- Produces: `TaskEditRequestPayload(string Title, string? Description, string Priority, DateOnly? DueDate,
  decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent)`,
  `CreateTaskEditRequestCommand(Guid TaskId, string Title, string? Description, string Priority,
  DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent, string? Reason)`.

- [ ] **Step 1: Write the failing validator test**

```csharp
using FluentValidation.TestHelper;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskEditRequestCommandValidatorTests
{
    private readonly CreateTaskEditRequestCommandValidator _validator = new();

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ProgressPercent_OutOfRange_Fails(int percent)
    {
        var command = new CreateTaskEditRequestCommand(
            Guid.NewGuid(), "Title", null, "medium", null, null, null, percent, null);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProgressPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(null)]
    public void ProgressPercent_InRangeOrNull_Passes(int? percent)
    {
        var command = new CreateTaskEditRequestCommand(
            Guid.NewGuid(), "Title", null, "medium", null, null, null, percent, null);

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.ProgressPercent);
    }

    [Fact]
    public void Reason_TooLong_Fails()
    {
        var command = new CreateTaskEditRequestCommand(
            Guid.NewGuid(), "Title", null, "medium", null, null, null, null, new string('a', 1001));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter CreateTaskEditRequestCommandValidatorTests`
Expected: build error — `CreateTaskEditRequestCommand` doesn't have `ProgressPercent`/`Reason` positional
parameters yet.

- [ ] **Step 3: Extend the payload record**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

public sealed record TaskEditRequestPayload(
    string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours,
    int? StoryPoints, int? ProgressPercent);
```

- [ ] **Step 4: Extend the command record**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;

public sealed record CreateTaskEditRequestCommand(
    Guid TaskId, string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent, string? Reason
) : IRequest<Result<TaskEditRequestResponse>>;
```

- [ ] **Step 5: Add the two validator rules**

Add to `CreateTaskEditRequestCommandValidator`'s constructor, alongside the existing rules:

```csharp
        RuleFor(x => x.ProgressPercent).InclusiveBetween(0, 100).When(x => x.ProgressPercent.HasValue)
            .WithMessage("Progress percent must be between 0 and 100.");
        RuleFor(x => x.Reason).MaximumLength(1000).WithMessage("Reason must be 1000 characters or fewer.");
```

- [ ] **Step 6: Update the handler to build the extended payload and persist `Reason`**

In `CreateTaskEditRequestCommandHandler.Handle`, change the payload construction line and the entity
construction to also set `Reason`:

```csharp
        var payload = new TaskEditRequestPayload(
            request.Title.Trim(), request.Description?.Trim(), request.Priority, request.DueDate,
            request.EstimatedHours, request.StoryPoints, request.ProgressPercent);
```

and in the `entity = new TaskEditRequest { ... }` initializer, add:

```csharp
                Reason = request.Reason?.Trim(),
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test --filter CreateTaskEditRequestCommandValidatorTests`
Expected: PASS (all 5 cases).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskEditRequestPayload.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskEditRequest/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskEditRequestCommandValidatorTests.cs
git commit -m "feat(work): add ProgressPercent and Reason to the task edit-request payload"
```

---

### Task 3: Extend `EditTaskCommand` with `ProgressPercent` and `Reason`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandValidatorTests.cs` (create if
  it doesn't already exist; if it does, add to it)

**Interfaces:**
- Produces: `EditTaskCommand(Guid TaskId, string Title, string? Description, string Priority,
  DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent, string? Reason)`.
- Note: this task does **not** touch `EditTaskCommandHandler` — that's Part 3. This task only extends the
  command and its validator so Part 3 has something to consume.

- [ ] **Step 1: Write the failing validator test**

```csharp
using FluentValidation.TestHelper;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskCommandValidatorTests
{
    private readonly EditTaskCommandValidator _validator = new();

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ProgressPercent_OutOfRange_Fails(int percent)
    {
        var command = new EditTaskCommand(
            Guid.NewGuid(), "Title", null, "medium", null, null, null, percent, null);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProgressPercent);
    }

    [Fact]
    public void Reason_TooLong_Fails()
    {
        var command = new EditTaskCommand(
            Guid.NewGuid(), "Title", null, "medium", null, null, null, null, new string('a', 1001));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (build error, missing positional params)**

- [ ] **Step 3: Extend the command record**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;

public sealed record EditTaskCommand(
    Guid TaskId, string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent, string? Reason
) : IRequest<Result<WorkTaskResponse>>;
```

- [ ] **Step 4: Add the two validator rules**

```csharp
        RuleFor(x => x.ProgressPercent).InclusiveBetween(0, 100).When(x => x.ProgressPercent.HasValue)
            .WithMessage("Progress percent must be between 0 and 100.");
        RuleFor(x => x.Reason).MaximumLength(1000).WithMessage("Reason must be 1000 characters or fewer.");
```

- [ ] **Step 5: Run the test to verify it passes**

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandValidatorTests.cs
git commit -m "feat(work): add ProgressPercent and Reason to EditTaskCommand"
```

---

### Task 4: Thread the new fields through the API contracts and controller

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`

**Interfaces:**
- Consumes: `EditTaskCommand`/`CreateTaskEditRequestCommand` from Tasks 2–3.
- Produces: `EditTaskRequest`/`CreateTaskEditRequestRequest` with the 2 new optional fields, wired into
  their controller actions — this is what makes the fields reachable from the frontend (frontend spec §3).

- [ ] **Step 1: Extend the two request contracts**

In `TaskContracts.cs`:

```csharp
public sealed record EditTaskRequest(
    string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent, string? Reason);
```

```csharp
public sealed record CreateTaskEditRequestRequest(
    string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent, string? Reason);
```

- [ ] **Step 2: Update the two controller actions to pass the new fields through**

In `TasksController.Edit`:

```csharp
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditTaskCommand(
            id, request.Title, request.Description, request.Priority, request.DueDate,
            request.EstimatedHours, request.StoryPoints, request.ProgressPercent, request.Reason), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

In `TasksController.CreateEditRequest`:

```csharp
    public async Task<IActionResult> CreateEditRequest(
        Guid taskId, [FromBody] CreateTaskEditRequestRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskEditRequestCommand(
            taskId, request.Title, request.Description, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints,
            request.ProgressPercent, request.Reason), ct);

        return result.IsSuccess
            ? StatusCode(202, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 3: Build to confirm everything compiles end-to-end**

Run: `dotnet build`
Expected: succeeds. (`EditTaskCommandHandler`/`ApproveTaskEditRequestCommandHandler` still ignore the new
fields at this point — that's fine, they compile because the record fields exist; Part 3/4 make them do
something with the values.)

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs
git commit -m "feat(work): thread ProgressPercent and Reason through the edit and edit-request API contracts"
```

---

## Self-review checklist for this Part

- [ ] `TaskEditRequestPayload`, `CreateTaskEditRequestCommand`, `EditTaskCommand`,
  `CreateTaskEditRequestRequest`, `EditTaskRequest` all have matching field lists and matching field order
  for the shared fields (`ProgressPercent` last-but-one, `Reason` last, consistently).
- [ ] `EditTaskCommandHandler` and `ApproveTaskEditRequestCommandHandler` are **not** modified in this
  Part — confirm with `git diff` before committing Task 4 that neither handler file appears in the diff.
- [ ] Every new validator rule has a `.WithMessage(...)` matching this module's existing sentence-case,
  period-terminated convention.
