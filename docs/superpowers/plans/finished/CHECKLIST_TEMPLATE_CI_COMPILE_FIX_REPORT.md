# Checklist Template CI Compile Fix — Report

## Root cause

`ChecklistTemplatesController.cs` and `OnboardingChecklistTemplatesController.cs` were committed to `main`/the feature branch (commit `1d9803ce3c36ea390f375e90994ade62f5d8e587`, "position edit and checklist for onboarding") **without their dependencies**. That commit only contained:

```
src/ONEVO.Api/Contracts/CoreHr/ChecklistTemplates/ChecklistTemplateTaskRequest.cs
src/ONEVO.Api/Contracts/CoreHr/ChecklistTemplates/CreateChecklistTemplateRequest.cs
src/ONEVO.Api/Contracts/CoreHr/ChecklistTemplates/UpdateChecklistTemplateRequest.cs
src/ONEVO.Api/Controllers/Tenant/CoreHr/ChecklistTemplatesController.cs
src/ONEVO.Api/Controllers/Tenant/CoreHr/OnboardingChecklistTemplatesController.cs
src/ONEVO.Api/appsettings.Development.json
```

The six command/query namespaces the controllers `using`-import (`Commands.CreateChecklistTemplate`, `Commands.UpdateChecklistTemplate`, `Commands.ArchiveChecklistTemplate`, `Queries.GetChecklistTemplate`, `Queries.ListChecklistTemplates`, `Queries.GetOnboardingChecklistTemplateMatches`) — plus the domain/infrastructure/DI changes they depend on — were implemented in the same working session (see `CHECKLIST_TEMPLATE_BACKEND_FOUNDATION_REPORT.md` and `docs/superpowers/plans/2026-08-13-checklist-template-backend-foundation.md`) but were **never committed**, per that session's explicit "do not commit or push" instruction. They existed only as uncommitted working-tree changes/untracked files, invisible to any clean CI checkout.

**Classification: files existed locally but were entirely untracked (new) or committed-but-since-modified (existing entities/repos/DI), not a namespace mismatch.** Every namespace in the untracked files already matches the controllers' `using` statements exactly — no renaming was needed.

## Verification of root cause

- `git ls-files` on all six command/query folders returned nothing (untracked).
- `git show HEAD:...ChecklistTemplatesController.cs` succeeds — the controller **is** committed at HEAD.
- `git show --stat 1d9803c...` confirms that commit added only the controllers/contracts, nothing else.
- `dotnet build src/ONEVO.Api` on the untouched working tree (all uncommitted changes present) → **succeeds**. On a clean checkout of HEAD alone it would fail exactly as CI reports, since the six namespaces don't exist there.

## Scope note: unrelated in-flight work in the same working tree

The working tree also contains unrelated uncommitted changes from other sessions (Cloudflare R2 storage adapter fix, dev-smoke-tenant seat policy fix, PositionTemplatePacks architecture-test fix, storage quota fix — each with its own `*_REPORT.md`). These are **not** part of this fix and were deliberately excluded from staging. Isolation was verified by stashing them out and confirming `dotnet build`/`dotnet test` for the checklist scope still pass without them (see Verification below), then restoring them unchanged.

Left untouched (still modified/untracked in the working tree, not staged):
- `src/ONEVO.Infrastructure/ExternalServices/Storage/CloudflareR2/CloudflareR2ObjectStorageAdapter.cs`
- `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`
- `tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs`
- `tests/ONEVO.Tests.Unit/Fakes/FakeStorageQuotaService.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/SeatEntitlement/SeatEntitlementServiceTests.cs`
- `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Storage/StorageQuotaServiceTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Storage/File/CloudflareR2ObjectStorageAdapterTests.cs` (untracked)
- `EMPLOYEE_ONBOARDING_DEV_SEAT_POLICY_FIX_REPORT.md`, `LEGAL_ENTITY_LOGO_R2_502_DIAGNOSTIC_REPORT.md`, `POSITION_TEMPLATE_PACKS_ARCHITECTURE_TEST_FIX_REPORT.md`, `STORAGE_QUOTA_LOCAL_LOGO_UPLOAD_FIX_REPORT.md` (untracked)

The 3 pre-existing `PositionPart2A`/`PositionPart2B` architecture-test failures (`PositionTemplatePacksController` collection-size assertions) are confirmed pre-existing and unrelated: they fail identically against HEAD's committed `PositionTemplatePacksController` regardless of whether the checklist fix is applied, because the *test files* that would reconcile those assertions are themselves part of the unrelated, un-staged PositionTemplatePacks work.

## Fix applied

`git add`'d (staged, **not committed** — per instructions, commit/push await explicit user approval) exactly the checklist-template feature's files: the 6 new command/query folders, their new supporting Application/Infrastructure files (Models, ServiceInterfaces, Services, DTO), the new EF migration pair, and the pre-existing tracked files whose modifications the new code requires (domain entities, repository interface/implementation, EF configurations, model snapshot, DI registration, and the two finalize/approve handlers whose call signatures changed) — plus the matching test files and the implementation plan doc.

### Exact files staged for commit (48 total)

**New (`A`) — Application layer**
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/CreateChecklistTemplate/CreateChecklistTemplateCommand.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/CreateChecklistTemplate/CreateChecklistTemplateCommandHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/CreateChecklistTemplate/CreateChecklistTemplateCommandValidator.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/UpdateChecklistTemplate/UpdateChecklistTemplateCommand.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/UpdateChecklistTemplate/UpdateChecklistTemplateCommandHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/UpdateChecklistTemplate/UpdateChecklistTemplateCommandValidator.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ArchiveChecklistTemplate/ArchiveChecklistTemplateCommand.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ArchiveChecklistTemplate/ArchiveChecklistTemplateCommandHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/GetChecklistTemplate/GetChecklistTemplateByIdQuery.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/GetChecklistTemplate/GetChecklistTemplateByIdQueryHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListChecklistTemplates/ListChecklistTemplatesQuery.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListChecklistTemplates/ListChecklistTemplatesQueryHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListChecklistTemplates/ListChecklistTemplatesQueryValidator.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/GetOnboardingChecklistTemplateMatches/GetOnboardingChecklistTemplateMatchesQuery.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/GetOnboardingChecklistTemplateMatches/GetOnboardingChecklistTemplateMatchesQueryHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/GetOnboardingChecklistTemplateMatches/GetOnboardingChecklistTemplateMatchesQueryValidator.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Models/ChecklistTaskContract.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/ServiceInterfaces/IChecklistTemplateAssigneeResolver.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Services/ChecklistTemplateHandlerSupport.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Services/ChecklistTemplateTaskInputResolver.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/DTOs/Responses/ChecklistTemplateResponse.cs`

**New (`A`) — Infrastructure**
- `src/ONEVO.Infrastructure/Services/CoreHr/Onboarding/ChecklistTemplateAssigneeResolver.cs`
- `src/ONEVO.Infrastructure/Migrations/20260813092025_AddChecklistTemplateScopeAndTaskRequiredFlag.cs`
- `src/ONEVO.Infrastructure/Migrations/20260813092025_AddChecklistTemplateScopeAndTaskRequiredFlag.Designer.cs`

**New (`A`) — Tests**
- `tests/ONEVO.Tests.Architecture/ChecklistTemplatesControllerArchitectureTests.cs`
- `tests/ONEVO.Tests.Integration/CoreHr/ChecklistTemplate/ChecklistTemplatesIntegrationTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ArchiveChecklistTemplateCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ChecklistTaskJsonContractTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ChecklistTemplateAssigneeResolverTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/CreateChecklistTemplateCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/CreateChecklistTemplateCommandValidatorTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/GetAndListChecklistTemplateQueryHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/GetOnboardingChecklistTemplateMatchesQueryHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/UpdateChecklistTemplateCommandHandlerTests.cs`

**New (`A`) — Docs**
- `docs/superpowers/plans/2026-08-13-checklist-template-backend-foundation.md`

**Modified (`M`) — pre-existing tracked files the new code requires**
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommandHandler.cs`
- `src/ONEVO.Application/Features/CoreHr/Onboarding/RepositoryInterfaces/IOnboardingPersistenceRepositories.cs`
- `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs`
- `src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/ChecklistTemplate.cs`
- `src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/EmployeeChecklistTask.cs`
- `src/ONEVO.Infrastructure/DependencyInjection.cs`
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Onboarding/ChecklistTemplateConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Onboarding/EmployeeChecklistTaskConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingPersistenceRepositories.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OnboardingPersistenceRepositoryTests.cs`
- `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs`

All 48 are currently `git add`-staged in this working tree. **No commit or push has been made** — awaiting explicit approval per instructions.

`CHECKLIST_TEMPLATE_BACKEND_FOUNDATION_REPORT.md` (the prior session's own implementation report) was left untracked/unstaged — it's documentation of the same work but wasn't required by the "make it compile" mandate; stage it too if you want the paper trail committed alongside the code.

## Migration decision

Migration files were inspected and confirmed **absent from CI** (untracked, matching the "missing entirely" condition in the task instructions), and the checklist repository/configuration code depends on the columns they add (`checklist_templates.legal_entity_id`, `checklist_templates.position_id`, `employee_checklist_tasks.is_required`). Per instructions, they were therefore included. `ApplicationDbContextModelSnapshot.cs` was included alongside them since EF requires the snapshot and migration to stay in sync — omitting one without the other would itself break the build/`dotnet ef` consistency check.

## Verification

| Command | Result |
|---|---|
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release` (full working tree) | **Build succeeded**, 0 errors, 1 pre-existing nullable warning |
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release` (checklist scope only — unrelated files stashed out) | **Build succeeded**, 0 errors — confirms checklist fix is self-sufficient, doesn't secretly depend on the unrelated storage/R2/position work |
| `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release` (checklist scope only) | **586/589 passed.** 3 failures are the pre-existing `PositionPart2A`/`PositionPart2B` `PositionTemplatePacksController` assertions — confirmed unrelated (fail identically at HEAD regardless of this fix) and out of scope per instructions |
| `dotnet test tests/ONEVO.Tests.Unit` filtered to Checklist/ApproveAccessGrantRequest/FinalizeOnboardingDraft/OnboardingPersistenceRepository | **100/100 passed** |
| `git diff --check` | No trailing-whitespace/conflict-marker errors (only benign LF→CRLF autocrlf notices) |
| Working-tree isolation check (stash unrelated files, rebuild, restore) | Unrelated files restored intact via `git stash pop`; `git status` confirms no loss |

`dotnet test tests/ONEVO.Tests.Integration --filter ChecklistTemplatesIntegrationTests` was not re-run (Docker Desktop dependency, same environmental blocker as the original implementation session) — the file is staged and compiles as part of the successful `dotnet build`/architecture-test runs above.

## Remaining risks

1. **Unrelated uncommitted work still sitting in the same working tree** (R2 storage adapter, dev-smoke-tenant seeder, PositionTemplatePacks architecture tests, storage quota) is orthogonal to this fix but will keep affecting `git status`/future diffs until those sessions' own changes are committed or discarded. Not touched here, per scope.
2. The 3 pre-existing `PositionPart2A`/`PositionPart2B` architecture-test failures remain **unresolved** — they are a separate, already-known issue (see `POSITION_TEMPLATE_PACKS_ARCHITECTURE_TEST_FIX_REPORT.md`) and were explicitly out of scope for this task.
3. Integration tests for the checklist feature (RLS, real-Postgres CRUD/matching) were not executed in this session (Docker unavailable) — same caveat as the original implementation report.
4. Nothing has been committed or pushed. CI will still fail until these 48 staged files are committed (and pushed) — that action requires explicit user approval per instructions.
