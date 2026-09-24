# Part 1: Drop required Release Date + add Banner Image to Project creation

**Read first:** `docs/superpowers/specs/next/2026-08-20-work-management-project-page-redesign-design.md`
(full design + why). This part is self-contained — you don't need the sibling part files to complete it.

**Scope guard:** Work Management module only. Do not touch, rebuild, or run anything outside
`ONEVO.*/Features/WorkManagement/**`, `ONEVO.Api/Controllers/Tenant/WorkManagement/**`, and the specific
shared files this part names below. Do not kill any running process that isn't yours.

**Status:** done (backend)

## Goal

1. `CreateProjectCommand.ReleaseDate` becomes optional. When omitted, the "Initial Release"
   `ReleaseCalendarEntry.ScheduledDate` defaults to `TargetDate`.
2. Add a second, independent optional image upload — Banner — alongside the existing Logo, with its
   own serve endpoint. Mirror the existing Logo plumbing exactly; do not merge banner into the logo field.

## Files you will touch or create

**Modify:**
- `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommand.cs`
- `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs`
- `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`
- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (Create action — add banner
  form field binding; new banner-serve action)
- `src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectCreationResponse.cs`
  (and whatever sibling DTO carries `ProjectLogoSummaryDto` — add an equivalent for banner)
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateProjectCommandHandlerTests.cs` (existing —
  extend, don't replace)
- `docs/postman-request/Work Management/Create Project.md` if it exists (update); otherwise this is
  covered by whichever doc already documents Create Project — update it, don't create a duplicate.

**Create:**
- `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectBanner/GetProjectBannerQuery.cs`
- `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectBanner/GetProjectBannerQueryHandler.cs`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetProjectBannerQueryHandlerTests.cs`
- `docs/postman-request/Work Management/Get Project Banner.md`

Before writing any of the above, open `GetProjectLogoQuery.cs` + `GetProjectLogoQueryHandler.cs` and
`ProjectsController.cs`'s existing logo-serve action in full — the banner query/handler/endpoint must be
a line-for-line structural mirror (same access-check pattern: `projects:read`/`*` permission OR active
project membership, same `IFileStorageService.OpenReadAsync` call), just swapping `ProjectCover` for the
new `ProjectBanner` purpose constant.

## Tasks (small, do in order, one commit per task)

1. **`UploadPurposeCatalog`**: add `public const string ProjectBanner = "project_banner";` and a
   `Rules` entry identical to `ProjectCover`'s (5MB, `ImageContentTypes`, `ImageExtensions`).
   - Test: extend whatever existing test covers `UploadPurposeCatalog.IsSupported`/`GetRule` (grep for
     it) to assert `ProjectBanner` resolves to the same rule shape as `ProjectCover`.

2. **`CreateProjectCommand`**: change `DateOnly ReleaseDate` → `DateOnly? ReleaseDate`. Add
   `string? BannerFileName, string? BannerContentType, Stream? BannerContent` (same three-field pattern
   already used for `LogoFileName`/`LogoContentType`/`LogoContent`).

3. **`CreateProjectCommandHandler` — release date default**: where `releaseReminder.ScheduledDate =
   request.ReleaseDate` is set, change to `request.ReleaseDate ?? request.TargetDate`.
   - Test: new test case — omit `ReleaseDate` in the command, assert the created
     `ReleaseCalendarEntry.ScheduledDate == request.TargetDate`.

4. **`CreateProjectCommandHandler` — banner upload**: mirror the existing `uploadedLogo` block exactly
   (same null-checks on the three Banner fields, same `_fileStorage.UploadAsync(...)` call with
   `UploadPurposeCatalog.ProjectBanner`, same failure-short-circuit `return Result<...>.Failure(...)`).
   Then mirror the `logoAsset` `EntityAsset` construction for a `bannerAsset` (`AssetPurpose =
   UploadPurposeCatalog.ProjectBanner`, `IsPrimary = true`), added via `_entityAssets.AddAsync` alongside
   `logoAsset`. Extend the `catch` block's orphaned-file logging to also cover an orphaned banner upload
   (same pattern, don't skip it — the existing comment explains why this can't be compensated).
   - Test: (a) create with both logo and banner — both `EntityAsset` rows exist with correct
     `AssetPurpose` values and both point at the project; (b) create with only banner, no logo — logo
     asset absent, banner asset present; (c) banner upload failure (invalid content type) — project is
     NOT created (whole operation short-circuits before the transaction), matching existing logo-failure
     test if one exists (mirror it).

5. **Response DTO**: add a banner-equivalent of `ProjectLogoSummaryDto` (or extend the same DTO with
   both `Logo` and `Banner` fields — check which is less invasive by reading the DTO first) so
   `ProjectCreationResponse` surfaces the banner file id/name the same way it does for logo today.

6. **`GetProjectBannerQuery` + Handler**: create as the mirror described above. Route:
   `GET api/v1/work/projects/{id}/banner`, same controller action shape as the existing logo-serve
   action (check its exact route/attributes first and copy them, only changing the purpose constant and
   the "Project banner not found." not-found message).
   - Test: mirror whatever test file covers `GetProjectLogoQueryHandler` (find it, extend the pattern):
     happy path streams the file, 404 when no banner asset exists, 403 when caller has neither
     `projects:read`/`*` nor active membership.

7. **`ProjectsController`**: wire the banner form fields into the multipart `Create` binding (same
   attribute pattern as the existing logo fields — `[FromForm]`, check the actual current binding
   approach first, don't guess), and add the new `GetBanner` action calling `GetProjectBannerQuery`.

8. **Postman doc**: update/extend the Create Project doc with the new optional banner fields and
   optional release date, and create `Get Project Banner.md` following the exact 6-section format rule
   6 requires (method+route, auth/permission/idempotency, description, request example, response
   example, error-status table, Source section linking the controller/handler files and this plan file).

## Data flow

`POST /work/projects` (multipart, now with optional `banner` file field, `releaseDate` now optional) →
`CreateProjectCommandHandler` validates category/identifier/labels → uploads logo (if present) → uploads
banner (if present) → creates `Project` + Default `Objective` + creator `ProjectMember` + `ProjectVersion`
("Initial Release", date = `ReleaseDate ?? TargetDate`) + `ReleaseCalendarEntry` + `Label`s + `EntityAsset`
row(s) for whichever of logo/banner were uploaded, all in one `SaveChangesAsync` → response includes both
image summaries (whichever exist). Later, `GET /work/projects/{id}/banner` streams the banner the same
way the existing logo endpoint does.

## Security

Banner serve endpoint uses the identical access rule as logo (`projects:read`/`*` permission OR active
project membership via `IProjectMemberRepository.HasActiveMembershipAsync`) — never looser. Upload size/
type validation is enforced by `UploadPurposeCatalog`'s rule the same way logo's already is; don't add a
separate ad-hoc check in the handler.

## Definition of done

- All 8 tasks committed individually (don't squash into one commit).
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green, including the
  new/extended tests above.
- Full solution `dotnet build` compiles clean.
- Both `docs/postman-request/Work Management/*.md` files reflect the current, real request/response shape
  (copy from the actual DTOs, don't hand-write from memory).
- Move this file's status to `finished` in `plans/SUMMARY.md`/`plans/next/SUMMARY.md` only once Part 2
  and Part 3 are also done (the whole `2026-08-20-work-management-project-page-redesign/` folder moves
  to `finished/<date>/` together, per `FILE_CREATION_RULES.md`).
