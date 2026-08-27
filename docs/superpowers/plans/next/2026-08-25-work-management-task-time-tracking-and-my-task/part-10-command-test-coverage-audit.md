# Part 10: Test coverage audit and hardening for every Command handler in this plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run last, after Parts 1–9 are fully implemented and their own tests pass. This Part is a
deliberate second pass over every Command handler's test file — not new features, no new endpoints. Its
job is to catch the specific, repeatable ways test suites for branching business logic like this fall
short: asserting only `IsSuccess` and skipping the actual side effect, missing the exact boundary value,
missing the "this must NOT happen" case, or copy-pasting one test into a second one without changing the
assertion. **Do not skip this Part or treat it as optional polish — it exists because exactly this kind of
gap has happened before on this project.**

**Why this Part exists, explicitly:** Parts 1–9 already include tests for their own new code, written
TDD-first. This Part is not about writing tests that don't exist yet — it's a **checklist-driven re-read**
of every test file this plan touches, checking each one against the matrix below and filling in whatever's
missing. Treat every unchecked box below as a real gap until you've confirmed a test for it exists and
passes, not as a suggestion.

## How to work through this Part

For each handler section below:
1. Open its test file.
2. Go through every bullet under that handler. For each one, find the existing test that covers it, or
   confirm none does.
3. For anything missing, write the test (TDD: write it, watch it fail if it exposes a real gap in the
   implementation, or watch it pass immediately if the implementation already handles it correctly and only
   the test was missing — both outcomes are valid, but you must actually run it, not assume).
4. Check the box.
5. Commit per handler section (one commit per section below, not one giant commit at the end — makes this
   Part's own diff reviewable).

## General rules that apply to every test you touch or add in this Part

- **Never assert only `result.IsSuccess`.** Every test must also assert the actual state change (or its
  absence) — the entity's field values, the fake repository's captured `.Added`/`.Updated` calls, or (for a
  rejection) that nothing was added/changed at all.
- **Every "this must NOT happen" behavior needs its own dedicated negative test.** A suite that only tests
  the positive path will stay green even if the guard preventing the negative case is deleted. If a handler
  conditionally writes a log row, you need one test proving it writes when it should AND one test proving
  it does not write when it shouldn't — not just the first.
- **Test the exact boundary value, not just "clearly inside" and "clearly outside."** This plan is full of
  `>`/`>=`/`==` comparisons (percent strictly greater, percent exactly 100, `InclusiveBetween(0, 100)`).
  Off-by-one is the single most likely bug class here. If a rule is "greater than," test the exact current
  value (must fail) and current value + 1 (must pass) — not 0 and 999.
- **Fakes must be fresh per test.** If an arrange-helper returns a fake repository with a `.Added` list,
  confirm each test gets its own new instance, not a shared static one — a leaking fake makes a later test
  pass by accident because an earlier test already populated its state.
- **When a rule differs by branch (e.g. direct edit vs. approved-request attribution), write both branches
  as separate explicit tests.** Do not write one and assume the other is symmetric — Part 4's whole point
  was that the two branches are *not* symmetric (attribution differs).
- **A test that already passes before you touch the implementation is not testing anything new.** If
  you're filling a genuine gap, it should fail first against the current code, or you've misunderstood what
  it's supposed to prove.

---

### Task 1: `EditTaskCommandHandler` (Part 3)

**File:** `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs`

- [ ] Not authenticated → `Forbidden`.
- [ ] No employee record for caller → `Forbidden`.
- [ ] Task not found → `NotFound`.
- [ ] Sprint achieved/frozen → `Forbidden` (pre-existing behavior — confirm a test for it still exists and
  still passes after this plan's changes, since this Part's diff touches the same handler).
- [ ] `EstimatedHours` exceeding available slack → `Conflict` with the slack value in the response body
  (pre-existing — same confirm-still-covered note).
- [ ] `ProgressPercent = -1` → validator rejects (already in Part 2's validator test file — confirm it's
  there, don't re-add).
- [ ] `ProgressPercent = 101` → validator rejects.
- [ ] `ProgressPercent = 0` exactly → **handler-level** test (not just validator): accepted, and if the
  task's prior value was higher, a `TaskPercentageLog` is written with `NewPercent = 0`.
- [ ] `ProgressPercent = 100` exactly → accepted, `TaskPercentageLog.NewPercent = 100`.
- [ ] `ProgressPercent` not supplied at all (`null`) → task's stored percent is unchanged, **and** no
  `TaskPercentageLog` row is written (the two are separate assertions — a test that only checks the task's
  value could pass even if a spurious no-op log row were written).
- [ ] `ProgressPercent` supplied but equal to the task's current value → no `TaskPercentageLog` row written
  (this is the boundary case most likely to be missed: "supplied but unchanged" is a distinct condition
  from "not supplied," and the handler's `!=` check must catch both the same way — write a test where
  `request.ProgressPercent.Value == task.ProgressPercent` explicitly, not just `null`).
- [ ] Only `Title` changes → `TaskEditLog.NewValuesJson` contains exactly one key (`title`), not five.
  Deserialize the JSON in the assertion and check the key count, not just `Contains("title")` — a
  substring check would pass even if the diff wrongly included every field.
- [ ] `Title` and `ProgressPercent` both change in the same request → **both** `TaskEditLog` (with both
  keys in the diff) and `TaskPercentageLog` are written in the same call, and their `ChangedAt` values are
  identical (same `now` reused, per Part 3's own implementation note — a passing test here is what actually
  confirms that note was followed, not just read).
- [ ] No field changes at all (request identical to current task state) → zero `TaskEditLog` rows, zero
  `TaskPercentageLog` rows (already in Part 3's plan as one test — confirm it exists, this is the most
  important negative case in this handler).
- [ ] `TaskEditLog.EmployeeId` and `TaskPercentageLog.EmployeeId` both equal the **caller's** resolved
  employee id — assert this explicitly by using two different fake employee ids for "caller" and some
  unrelated id in the fixture, so the assertion can't pass by both sides accidentally defaulting to the
  same `Guid.Empty`.

Commit:
```bash
git add tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs
git commit -m "test(work): harden EditTaskCommandHandler test coverage per Part 10 audit"
```

---

### Task 2: `ApproveTaskEditRequestCommandHandler` (Part 4)

**File:** `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ApproveTaskEditRequestCommandHandlerTests.cs`

- [ ] Not authenticated / no employee record → `Forbidden` (pre-existing — confirm still covered).
- [ ] Request not found → `NotFound`.
- [ ] Request already `Approved` or `Rejected` (not `Pending`) → `Conflict`, and **the task is not
  mutated** — assert the task's fields are unchanged, not just that the result is a conflict.
- [ ] Task not found / Objective not found → `NotFound` (pre-existing).
- [ ] Caller is not the effective manager → `Forbidden`, task unmutated (pre-existing).
- [ ] Sprint frozen → `Conflict` (pre-existing).
- [ ] `EstimatedHours` slack conflict → `Conflict` (pre-existing).
- [ ] **Approving twice** (call `Handle` a second time with the same now-`Approved` request) → second call
  returns `Conflict`, and does **not** write a second `TaskEditLog`/`TaskPercentageLog` pair — this is an
  idempotency case not explicitly listed in Part 4's own plan text; add it here.
- [ ] `TaskEditLog.EmployeeId` = `pending.RequestedByEmployeeId`, verified with a fixture where the
  requester's id and the approver's id are two different, explicitly distinct GUIDs (not both defaulted) —
  Part 4 already has this test; confirm it uses genuinely different ids, not two calls to `Guid.NewGuid()`
  that happen to differ only by accident of the fixture (i.e., the test must fail if the code were changed
  to use `callerEmployeeId` instead — mentally run that mutation against the test to confirm it would
  actually catch it).
- [ ] `TaskPercentageLog.EmployeeId` = `pending.RequestedByEmployeeId`, same distinctness requirement.
- [ ] `payload.ProgressPercent` equal to the task's current value → no `TaskPercentageLog` written (same
  boundary as Task 1's equivalent case — Part 4's plan only tested `null`, not "supplied-but-unchanged";
  add the missing case).
- [ ] Payload changes multiple fields → `TaskEditLog` diff contains exactly the changed keys, none else
  (same JSON-key-count assertion style as Task 1).

Commit:
```bash
git add tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ApproveTaskEditRequestCommandHandlerTests.cs
git commit -m "test(work): harden ApproveTaskEditRequestCommandHandler test coverage per Part 10 audit"
```

---

### Task 3: `MoveTaskStatusCommandHandler` (Part 5)

**File:** `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs`

- [ ] Not authenticated / no employee record → `Forbidden` (pre-existing).
- [ ] Task not found / target status not found / target status belongs to a different project →
  `NotFound` (pre-existing).
- [ ] Objective not found → `NotFound` (pre-existing).
- [ ] Caller is neither effective manager nor active member → `Forbidden` (pre-existing).
- [ ] Caller is an active member but not effective manager, moving into a **private**-visibility status →
  `Forbidden` (pre-existing).
- [ ] Caller is an active member but not effective manager, moving into a **public**-visibility status →
  succeeds (pre-existing — confirm the positive case alongside the negative one above; a suite testing
  only the rejection could hide a bug where *everyone* gets rejected).
- [ ] Sprint frozen → `Forbidden` (pre-existing).
- [ ] `TaskStatusChangeLog` is written on **every** successful move, including moves that don't touch
  completion at all (e.g. "To Do" → "In Progress", neither marking complete) — this is the case most
  likely to be missed, since it's easy to only test the log alongside the completion-flip tests. Write it
  as its own standalone test with both statuses' `MarksTaskComplete = false`.
- [ ] Move between two statuses that **both** have `MarksTaskComplete = true` → neither
  `wasComplete && !willBeComplete` nor `!wasComplete && willBeComplete` fires (both are `true`) → no
  `TaskPercentageLog` row, but the `TaskStatusChangeLog` row still is written. This exact edge case is not
  in Part 5's own plan text — add it; it's a legitimate configuration (two different "done" statuses) this
  handler must handle without crashing or double-logging.
- [ ] `FromStatusId`/`ToStatusId` on the log row are not swapped — construct the fixture with two
  distinguishable status ids and assert each lands in the correct field, not just that both are present
  somewhere.
- [ ] 0→100 flip: `TaskPercentageLog.PreviousPercent == 0`, `NewPercent == 100`, regardless of what the
  task's `ProgressPercent` was actually set to going in (the existing code hardcodes 0/100 for this
  handler's flip — a test should confirm this is true even if the task entered with some other stale value
  like 45%, to catch a future regression where someone "fixes" this to read the actual prior value instead
  of hardcoding).

Commit:
```bash
git add tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs
git commit -m "test(work): harden MoveTaskStatusCommandHandler test coverage per Part 10 audit"
```

---

### Task 4: `ClockInTaskCommandHandler` (Part 6)

**File:** `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ClockInTaskCommandHandlerTests.cs`

- [ ] Not authenticated / no employee record → `Forbidden`.
- [ ] Task not found → `NotFound`.
- [ ] Caller is not an assignee → `Forbidden`, and no session is added (Part 6 already has this test —
  confirm the "no session added" half is actually asserted, not just the `403`).
- [ ] `task.ProgressPercent == 100` exactly → `Conflict`, no session added.
- [ ] `task.ProgressPercent == 99` → succeeds (the boundary pair to the case above — Part 6's own plan only
  tested `100`; add `99` as the adjacent passing case so an `>=` vs `==` mistake in the lock check would be
  caught).
- [ ] Task already has an open session belonging to the **same** caller → `Conflict` (not silently treated
  as "already clocked in, no-op success" — the rule is one open session per task, full stop, regardless of
  who holds it).
- [ ] Task already has an open session belonging to a **different** employee → `Conflict` (Part 6 covers
  this already — confirm).
- [ ] Happy path: the added session's `TaskId`, `EmployeeId`, `ClockInAt` (non-default, roughly "now"), and
  `ClockOutAt == null` are all asserted individually, not just that one session exists.
- [ ] Order-of-checks: construct a fixture that would fail **both** the assignee check and the lock check
  simultaneously (not an assignee, AND task at 100%) — assert it returns `403` (assignee check), not `409`
  (lock check), confirming the handler's actual check order rather than assuming it from reading the code.

Commit:
```bash
git add tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ClockInTaskCommandHandlerTests.cs
git commit -m "test(work): harden ClockInTaskCommandHandler test coverage per Part 10 audit"
```

---

### Task 5: `PushTaskCommandHandler` (Part 6)

**File:** `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/PushTaskCommandHandlerTests.cs`

- [ ] Not authenticated / no employee record → `Forbidden`.
- [ ] Task not found → `NotFound`.
- [ ] No open session on the task → `Conflict`.
- [ ] Open session exists but belongs to a different employee → `Forbidden`, and the session is **not**
  closed (assert `ClockOutAt` is still `null` afterward — Part 6 already asserts this, confirm it's really
  there).
- [ ] `Percent == task.ProgressPercent` exactly → `400`, session not closed, no log written (Part 6 covers
  the "not greater" case generally — add this as the exact-equal boundary specifically, distinct from
  Percent one below current).
- [ ] `Percent == task.ProgressPercent - 1` → `400` (the "clearly less" case, for completeness alongside
  the exact-equal boundary above).
- [ ] `Percent == task.ProgressPercent + 1` → succeeds (the tight boundary above the rejection — proves the
  comparison is `<=` rejects / `>` accepts, not off by one in either direction).
- [ ] `Percent == 100` → succeeds, and the response's `ProgressPercent == 100` (Part 6 has a version of
  this test already — confirm it also checks the response value, not just `IsSuccess`).
- [ ] Duration: use a fixed/injectable clock if this test file's convention supports one, or assert
  `DurationMinutes` falls within a tolerance window rather than an exact value (Part 6's own test example
  already does this — `>= 44 && <= 46` for a 45-minutes-ago clock-in — confirm every duration assertion in
  this file uses a tolerance, not an exact equality that will occasionally flake).
- [ ] `TaskPercentageLog.ClockingSessionId` equals the **specific session that was open and just got
  closed** — construct a fixture with a second, already-closed, older session on the same task in the
  fixture data, and assert the new log's `ClockingSessionId` points at the newly-closed one, not the old
  one (this catches a bug where the handler might grab "any session for this task" instead of "the one
  open session").
- [ ] Reason supplied → saved verbatim, trimmed. Reason omitted (`null`) → log's `Reason` is `null`, not an
  empty string (checking `null` vs `""` explicitly matters here, since a UI might render an empty string as
  a visible-but-blank note instead of hiding the field).
- [ ] Multiple historical closed sessions on the same task (simulating a task that's had several
  Clock-in/Push cycles already) → a new Clock-in/Push cycle still works correctly and only affects the
  newly-opened session, never touching the historical ones' `DurationMinutes`/`ClockOutAt`.

Commit:
```bash
git add tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/PushTaskCommandHandlerTests.cs
git commit -m "test(work): harden PushTaskCommandHandler test coverage per Part 10 audit"
```

---

### Task 6: `AddClockingSessionReasonCommandHandler` / `AddPercentageLogReasonCommandHandler` (Part 6)

**Files:**
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AddClockingSessionReasonCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AddPercentageLogReasonCommandHandlerTests.cs`

For **both** files:
- [ ] Not authenticated / no employee record → `Forbidden`.
- [ ] Row not found → `NotFound`.
- [ ] Caller does not own the row (`EmployeeId` mismatch) → `Forbidden`, and `Reason` is unchanged
  afterward (Part 6 already tests this — confirm the "unchanged afterward" half specifically, not just the
  status code).
- [ ] Happy path → `Reason` set exactly to the trimmed input value.
- [ ] Calling it a **second time** on a row that already has a reason → overwrites with the new value (this
  is not explicitly specified anywhere — confirm this is the actual intended behavior by checking the
  handler's implementation; if it silently overwrites, that's probably fine and just needs a test proving
  it; if the product intent was actually "reason is set-once," that's a real product question to flag to
  the user rather than silently deciding either way — do not guess, ask).

Commit:
```bash
git add tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AddClockingSessionReasonCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AddPercentageLogReasonCommandHandlerTests.cs
git commit -m "test(work): harden reason-note command handler test coverage per Part 10 audit"
```

---

### Task 7: `GetTaskHistoryQueryHandler` and `GetMyProjectTasksQueryHandler` (Parts 7–8, queries not commands, included for completeness)

**Files:**
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskHistoryQueryHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetMyProjectTasksQueryHandlerTests.cs`

- [ ] History: a task with **two separate** Clock-in/Push cycles (two closed sessions, each with its own
  matching `TaskPercentageLog`) → exactly two `clock_session` entries, each correctly paired with its own
  push data — not cross-matched (session A's percentage showing on session B's entry). Construct a fixture
  where session A pushed to 40% and session B (later) pushed to 70%, and assert each entry's
  `PushedPercent` matches its own session, not the other one's.
- [ ] History: employee id with no resolvable display name → falls back to `"A teammate"` (matches the
  existing fallback convention used elsewhere in this module, e.g.
  `CreateTaskEditRequestCommandHandler.cs:78` — confirm `GetTaskHistoryQueryHandler` uses the identical
  fallback string, not a different one).
- [ ] History: task not found → `NotFound`.
- [ ] My-tasks: project not found or inactive → `NotFound`.
- [ ] My-tasks: a task assigned to the caller in a **different** project is not returned, even if the
  caller passes that other project's id by mistake — construct two tasks with the same assignee across two
  different projects and assert only the requested project's task comes back (cross-project isolation,
  worth an explicit test even though `GetByProjectAsync` already scopes by project — a future refactor of
  that repository method is exactly the kind of change this test should catch).
- [ ] My-tasks: `sprintId` filter with a sprint that has zero of the caller's tasks → returns an empty list
  successfully, not an error.

Commit:
```bash
git add tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskHistoryQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetMyProjectTasksQueryHandlerTests.cs
git commit -m "test(work): harden history and my-tasks query handler test coverage per Part 10 audit"
```

---

## Self-review checklist for this Part

- [ ] Every box above is either checked with a real passing test behind it, or you stopped and asked the
  user a product question (Task 6's set-once-vs-overwrite question is the one case in this Part flagged as
  a genuine open question, not an implementation detail to decide silently).
- [ ] `dotnet test --filter FullyQualifiedName~WorkManagement` is green after this Part's final commit.
- [ ] No test added in this Part duplicates an assertion already made by an existing test under a new name
  — if you find yourself writing a test that's identical to one already in the file, that means the
  existing one already covered it; move on to the next unchecked box instead.
