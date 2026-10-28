# Work Mode Lookup Endpoint

## Endpoint added

`GET /api/v1/work-modes` on a new `WorkModesController`
(`src/ONEVO.Api/Controllers/Tenant/CoreHr/WorkModesController.cs`).

The task's suggested route (`GET /api/v1/work-modes`) was used as-is — there is no existing
tenant-scoped lookup/reference-data controller convention in this codebase to defer to instead
(the only precedent, `AdminReferenceController`, is Admin/Developer-Platform-scoped and not
reachable by tenant users). The closest tenant-side precedent for a flat, non-legal-entity-scoped
`GET` lookup is `PermissionsController` (`GET /api/v1/permissions`), and this endpoint follows its
exact shape: `[ApiController]`, `[Route("api/v1/work-modes")]`, `[Authorize(Policy =
"TenantPolicy")]` at the class level, a single `IMediator` constructor dependency, and
`result.IsSuccess ? Ok(...) : Problem(...)` in the action.

## Response contract

```json
[
  { "id": 1, "code": "on_site", "label": "On-Site" },
  { "id": 2, "code": "remote",  "label": "Remote"  },
  { "id": 3, "code": "hybrid",  "label": "Hybrid"  }
]
```

`id` is an `int` (matches `WorkMode.Id`, never a `Guid`). Values and labels are the actual rows
`LookupDataSeeder.WorkModes()` seeds — nothing invented. Only `IsActive == true` rows are returned;
`IsActive` itself is not included in the response, matching the task's sample payload. Ordered by
`Label` ascending (no `SortOrder` column exists on the actual `WorkMode` entity, unlike the
aspirational schema in `phase1-table-inventory.md`, which documents `id uuid` + `sort_order` for
`work_modes` — the real entity, migration, and seeder in this repo all use `int Id` with no
`SortOrder`, so `Label` is the only defensible ordering key available).

## Permission

`[RequirePermission("employees:write")]` under the controller's class-level
`[Authorize(Policy = "TenantPolicy")]`. This matches the task's stated preference directly — no
deviation needed. `employees:write` is the same permission `OnboardingDraftsController.Create`
already requires, so any tenant user who can start an Add Employee draft can also resolve work
modes for it.

## Repository

`IWorkModeRepository` (already existed, used by `SaveOnboardingDraftCommandHandler` for
`ExistsActiveAsync`) gained one new method:

```csharp
Task<List<WorkMode>> ListActiveAsync(CancellationToken ct = default);
```

`EfWorkModeRepository.ListActiveAsync`:

```csharp
_db.WorkModes.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Label).ToListAsync(ct);
```

`AsNoTracking`, as required. `work_modes` is genuine global seeded reference data — no
`tenant_id` column exists on the entity/table, and neither `ListActiveAsync` nor
`ExistsActiveAsync` takes a tenant parameter. `ListActiveWorkModesQuery` is a parameterless
record — no `TenantId` field exists to accept from a request body or query string, and a
dedicated test (`Query_HasNoTenantIdProperty`) plus an architecture test
(`ListAction_AcceptsNoTenantIdParameter`) both guard this.

`IWorkModeRepository` was already registered in `Infrastructure/DependencyInjection.cs`
(`AddScoped<IWorkModeRepository, EfWorkModeRepository>()`), so no DI change was needed. The new
`ListActiveWorkModesQueryHandler` needs no DI registration either — MediatR discovers it via
`services.AddMediatR(...)` assembly scanning, same as every other handler in this codebase.

## Files changed

**Application**
- `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/RepositoryInterfaces/IWorkModeRepository.cs` — added `ListActiveAsync`.
- `src/ONEVO.Application/Features/CoreHr/WorkMode/DTOs/Responses/WorkModeDto.cs` (new) — `record WorkModeDto(int Id, string Code, string Label)`.
- `src/ONEVO.Application/Features/CoreHr/WorkMode/Queries/ListActiveWorkModes/ListActiveWorkModesQuery.cs` (new).
- `src/ONEVO.Application/Features/CoreHr/WorkMode/Queries/ListActiveWorkModes/ListActiveWorkModesQueryHandler.cs` (new).

**Infrastructure**
- `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfWorkModeRepository.cs` — added `ListActiveAsync`.

**Api**
- `src/ONEVO.Api/Controllers/Tenant/CoreHr/WorkModesController.cs` (new).

**Tests**
- `tests/ONEVO.Tests.Unit/Features/CoreHr/WorkMode/EfWorkModeRepositoryTests.cs` (new, 3 tests): excludes inactive rows, orders by label (not by id/insertion order), returns integer ids matching seeded values.
- `tests/ONEVO.Tests.Unit/Features/CoreHr/WorkMode/ListActiveWorkModesQueryHandlerTests.cs` (new, 3 tests): maps repository rows to DTOs, returns an empty list when nothing is active, and asserts the query type carries no `TenantId` property.
- `tests/ONEVO.Tests.Unit/Features/CoreHr/WorkMode/WorkModesControllerTests.cs` (new, 3 tests): 200 with the mediator's value on success, sends `ListActiveWorkModesQuery`, and maps a failure `Result` to `Problem()` with the handler's status code.
- `tests/ONEVO.Tests.Architecture/WorkModesControllerArchitectureTests.cs` (new, 7 tests): namespace, `TenantPolicy`, exact route, `HttpGet` with no extra template, **`employees:write` on the action (reflection-based 403 gate check)**, no `tenantId` parameter, constructor injects `IMediator` only.

## Tests not written, and why

Per the task's own conditionals ("...if controller tests cover auth", "...if permission attributes
are testable"): this codebase's established convention (`OnboardingDraftsControllerTests.cs`,
`DepartmentsControllerArchitectureTests.cs`) is that `[Authorize]`/`[RequirePermission]` are
ASP.NET pipeline concerns — an authorization filter and a policy handler — that never run when a
controller action is invoked directly in a unit test (`_sut.List(...)`), so no existing controller
test in this repo asserts a literal 401/403 that way. Instead, the convention is a reflection-based
architecture test asserting the attribute and its exact permission string are present on the
action — that's what `WorkModesControllerArchitectureTests` does
(`Controller_RequiresTenantPolicy` for the 401 gate, `ListAction_RequiresEmployeesWritePermission`
for the 403 gate). This is a real, running, compiled check, not a placeholder — it would fail if
either attribute were removed or the permission string changed.

## Verification

- **Build** — all 7 projects, in dependency order, `--verbosity minimal`: `Domain` → `Application`
  → `Infrastructure` → `Api` → `Tests.Unit` → `Tests.Architecture` → `Tests.Integration`. **All 7
  succeed, 0 errors.** (One blocker hit and resolved: a stray `ONEVO.Api.exe` dev-server process,
  PID 34688, was locking `bin\Debug\net10.0\*.dll` and failing every build with MSB3027 — same
  class of issue a prior session on this branch recorded. Stopped with the user's explicit
  confirmation via `taskkill`; every build succeeded cleanly afterward.)
- **Focused unit tests** — `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-build
  --filter "FullyQualifiedName~WorkMode"` → **10/10 passed** (3 repository + 3 handler + 3
  controller + the 1 pre-existing `SaveOnboardingDraftCommandHandler` work-mode test the filter
  also matched, unaffected).
- **Full unit suite** (regression check) — `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj
  --no-build` → **1631/1631 passed, 0 failed.**
- **Focused architecture tests** — `--filter "FullyQualifiedName~WorkMode"` on
  `ONEVO.Tests.Architecture` → **7/7 passed.**
- **Full architecture suite** (regression check) — **555/555 passed, 0 failed.**
- **`git diff --check`** — run against both the already-tracked modified files and this session's
  new files (via `git add -N` so untracked new files are included, then unstaged again
  immediately — nothing was committed) → **exit code 0.** Only pre-existing LF→CRLF warnings
  (Windows checkout artifact, present on files this branch already had modified before this
  session) — no actual whitespace/conflict errors, including on the 8 new files this session added.

### Docker / integration tests

Docker **was** available this session (`docker info` succeeded, unlike several prior sessions'
reports on this branch). Ran `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj
--no-build --filter "FullyQualifiedName~OnboardingDraft"` as a real attempt, not a skip-by-default.
Testcontainers successfully started a Postgres container and the test host connected to it, but
all 4 matched tests failed with `Npgsql.PostgresException: 42501: permission denied for table
work_modes`.

**Confirmed pre-existing, not caused by this session's change:** every failing stack trace bottoms
out at `SaveOnboardingDraftCommandHandler.cs:50` calling `_workModeRepository.ExistsActiveAsync(...)`
— a method that already existed before this session (this session only added the new
`ListActiveAsync` method alongside it; `WorkModesController`/`ListActiveWorkModesQueryHandler` are
never on the call path for any of these 4 draft-save tests). The failure is a missing Postgres
`GRANT SELECT` (or similar RLS/role-privilege gap) on the `work_modes` table in whatever schema
script the integration test's Testcontainers fixture applies — unrelated to this endpoint and
unrelated to this session's repository/query/controller additions. **Not fixed here** (out of
scope: it's a pre-existing integration-test-environment gap on a query this session didn't write),
but flagged clearly rather than silently reported as "Docker unavailable." No integration test was
added for the new `GET /api/v1/work-modes` endpoint itself, since the existing fixture can't
currently exercise any `work_modes`-touching code path in this environment.

## Frontend contract note

`workModeId` is a **number** (`int`), never a string or GUID, both on this new lookup response's
`id` field and on `SaveOnboardingDraftRequest.WorkModeId` / `OnboardingDraftResponse.WorkModeId`
(unchanged by this session — already `int` from the prior backend-correction work). Part 2
frontend work should type `workModeId: number` throughout the Add Employee wizard's
models/API/store, matching the seeded values `1` (`on_site`, "On-Site"), `2` (`remote`, "Remote"),
`3` (`hybrid`, "Hybrid").
