# Checklist Template CI Integration Fix Report

## Scope

Fixed a CI-failing integration test assertion. No production code, migrations, or frontend
files were touched.

## Root cause

`ChecklistTemplatesIntegrationTests.InstantiateAsync_CreatesRealEmployeeChecklistTaskRows_AndNeverMutatesTheTemplate`
(around line 197) asserted that `ChecklistTemplate.TasksJson`, reloaded from Postgres after
instantiation, was byte-for-byte equal to the literal JSON string that was originally seeded:

```csharp
reloadedTemplateAfter.TasksJson.Should().Be(originalTasksJson);
```

`tasks_json` is stored as a `jsonb` column. Postgres normalizes `jsonb` values on write —
it may reorder object properties and strip/alter whitespace — so a value round-tripped through
the database is not guaranteed to come back as the same string that was inserted, even though
it represents the exact same data. This made the assertion brittle: it was asserting text
formatting, not data correctness, and could fail (or pass) depending on Postgres's internal
`jsonb` normalization rather than on any real bug.

## Confirmation production JSON content is equivalent

The test's intent — "the template row was never mutated by instantiation" — is a claim about
data, not text. `ChecklistTaskJsonContract.Parse` (`src/ONEVO.Application/Features/CoreHr/Onboarding/Models/ChecklistTaskContract.cs`)
is the single strict parser the application itself uses for template task JSON (shared by
template CRUD, draft edit validation, and instantiation). Re-parsing the reloaded template's
`TasksJson` through that same contract and asserting on the resulting fields confirms the data
is unchanged, independent of Postgres's `jsonb` formatting. No production code was modified —
this was purely a test-assertion issue, not a data contract or serialization bug.

## Exact test changed

File: `tests/ONEVO.Tests.Integration/CoreHr/ChecklistTemplate/ChecklistTemplatesIntegrationTests.cs`

- Added `using ONEVO.Application.Features.CoreHr.Onboarding.Models;`
- Replaced the raw string equality assertion on `TasksJson` with a semantic comparison via
  `ChecklistTaskJsonContract.Parse(reloadedTemplateAfter.TasksJson, ChecklistTaskDueRuleMode.OffsetDays)`,
  asserting:
  - exactly one task
  - `Title == "Complete profile"`
  - `OwnerType == ChecklistTaskOwnerTypes.Employee` ("employee")
  - `AssignedToId == null`
  - `DueOffsetDays == 2`
  - `IsRequired == true`
  - `DueDate == null`

No changes to serializer configuration, JSON property order, or the checklist JSON contract
itself.

## Verification

Docker Desktop was required (and started) for the Postgres-backed integration tests.

```bash
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ChecklistTemplatesIntegrationTests"
```
Result: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`

```bash
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release --no-restore
```
Result: `Passed! - Failed: 0, Passed: 590, Skipped: 0, Total: 590`

```bash
git diff --check
```
Result: exit code 0 (only an autocrlf LF/CRLF informational warning, no whitespace errors).
