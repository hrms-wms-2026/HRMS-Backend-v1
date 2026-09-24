# Position Template Packs Architecture Test Fix

## CI failure root cause

Three Position Part 2 architecture tests used string matches that had no word/namespace
boundary, so they incidentally caught the legitimate, separate `PositionTemplatePacks`
feature that ships alongside the original Position foundation:

1. `PositionPart2A_DoesNotExpose_Commands_Queries_Or_RequestContracts`
   filtered Application-assembly types with
   `t.Namespace?.Contains("OrgStructure.Position", StringComparison.Ordinal)`.
   `ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.*` contains the substring
   `"OrgStructure.Position"` (the `Position` in `PositionTemplatePacks`), even though it is not
   part of the original Part 2A/2B `OrgStructure.Position` scope. As a result the query/handler
   types under `PositionTemplatePacks.Queries.ListPositionTemplatePacks`, plus its DTOs and
   mapper, were flagged as forbidden leakage.

2. `PositionPart2C_Introduces_ExactlyOnePositionsController_InExpectedNamespace` and
3. `PositionsController_IntroducedInPart2C_IsTheOnlyPositionController`
   both selected API controllers with
   `t.Name.Contains("Position", ...) && t.Name.EndsWith("Controller", ...)`, i.e. "any
   controller whose name starts with/contains Position", instead of the specific
   `PositionsController` type. Once `PositionTemplatePacksController` was added as its own,
   legitimately separate OrgStructure controller, the collection had 2 items and
   `Assert.Single` failed.

None of this was a production defect: `PositionsController` and `PositionTemplatePacksController`
are two intentionally distinct controllers, and `PositionTemplatePacks.*` is an intentionally
distinct Application feature namespace from the original Position Part 2A/2B CQRS scope. The
tests' string matching was just broader than the English description of what they were meant to
guard.

## Tests changed

`tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs`:

- `PositionPart2A_DoesNotExpose_Commands_Queries_Or_RequestContracts` — added a second `.Where`
  that excludes any type whose namespace starts with
  `ONEVO.Application.Features.OrgStructure.PositionTemplatePacks`. The original
  `Contains("OrgStructure.Position")` match is left intact (still guards the original Part 2A/2B
  scope); only the new feature's namespace is carved out.
- `PositionPart2C_Introduces_ExactlyOnePositionsController_InExpectedNamespace` — changed the
  controller filter from `Name.Contains("Position") && Name.EndsWith("Controller")` to
  `Name.Equals("PositionsController", Ordinal)`, so it counts only the one controller it names in
  its assertion, not every `Position*Controller`.
- Added `PositionTemplatePacksController_IsASeparateControllerFromPositionsController` — a new,
  narrowly-scoped fact asserting `PositionTemplatePacksController` exists in the expected
  OrgStructure namespace and is a distinct type from `PositionsController`, so the "exactly one
  PositionsController" guard above can't be quietly satisfied by renaming/merging the new
  controller into the old one.

`tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs`:

- `PositionsController_IntroducedInPart2C_IsTheOnlyPositionController` — same fix as above:
  `Name.Contains("Position")` → `Name.Equals("PositionsController", Ordinal)`.

No test was removed or weakened in scope beyond excluding the one new, legitimate feature
namespace/controller; all original Position Part 2A/2B protections (namespace, route
conventions, forbidden CQRS/DTO leakage, no ApplicationDbContext injection, no stray enums, no
`Guid.Empty` fallbacks, block-bodied repository members, etc.) are unchanged.

## Why this was a brittle test assumption, not a production defect

The original tests were written when Position Part 2 was the only "Position*" thing in
OrgStructure, so `Contains("Position")` and `Contains("OrgStructure.Position")` were sufficient
proxies for "the Position foundation scope." They encoded that assumption as a substring match
instead of an exact name/namespace match. `PositionTemplatePacksController` and
`PositionTemplatePacks.*` are a new, deliberately separate OrgStructure feature (own controller,
own Application namespace) — correct by design — that simply happened to share the `Position`
prefix, which is what tripped the substring checks.

## Verification

```
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release
```
Result: `Passed! - Failed: 0, Passed: 590, Skipped: 0, Total: 590` (was 571 passed / 3 failed
before the fix; +19 due to the pre-existing suite plus the one new test added here).

```
dotnet build
```
Result: builds clean (only pre-existing, unrelated nullable-reference warnings in
`AdminAuthController.cs`, `TenantRlsInterceptorTests.cs`, `PermissionSeederTests.cs`,
`GetPositionTreeQueryHandlerTests.cs`).

```
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --filter "FullyQualifiedName~PositionTemplatePacks"
```
Result: `Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12` (covers
`ListPositionTemplatePacksQueryHandlerTests`, `EfConfigurationTemplateRepositoryPositionTemplateFilterTests`,
`PositionTemplatePackSeederTests`).

```
git diff --check
```
Result: no reported whitespace errors (only pre-existing LF→CRLF autocrlf notices on files
already modified elsewhere in the working tree, unrelated to this change).

```
git status --short -- tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs
```
Confirmed only these two files were modified by this fix.

## Staged vs. unstaged status

As of this verification pass, both changed files are **modified but unstaged** (working tree
only, not in the index):

```
git status --short --branch
```
```
## feature/employee-management-phase1-foundation
 M tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs
 M tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs
```
`git diff --cached` for both files is empty. Per instructions, nothing was staged or committed —
the fix is left as unstaged working-tree changes pending user approval.

## Skipped checks

- Did not run the full `ONEVO.Tests.Unit` or `ONEVO.Tests.Integration` suites — the working tree
  already contains unrelated in-progress changes (Checklist Template backend foundation, R2/seat
  policy fixes, etc.) outside this task's scope, so a full-suite run would mix in unrelated
  pass/fail signal. Ran the full architecture suite (in scope) and the focused
  `PositionTemplatePacks` unit tests (explicitly requested) instead.
- Did not touch migrations, seeders, API routes, or any production `PositionTemplatePacks` code —
  inspection confirmed the controller and Application namespace placement are correct as-is; no
  misplacement was found.
