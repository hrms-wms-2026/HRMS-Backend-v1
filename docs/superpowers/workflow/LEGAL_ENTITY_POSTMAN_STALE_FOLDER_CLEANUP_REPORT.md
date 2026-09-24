# Legal Entity / Company — Postman Stale Folder Cleanup Report

**Scope:** Postman collection cleanup only. No backend source, tests, migrations, or docs were touched. Nothing was staged, committed, or pushed.
**Repo:** `C:\onevoNew\HRMS-Backend-v1`

---

## 1. Files Changed

**Deleted (4 files, entire stale folder):**
- `postman/collections/ONEVO Organization Admin API/07. Organization - Company/Create Legal Entity.request.yaml`
- `postman/collections/ONEVO Organization Admin API/07. Organization - Company/Get Legal Entity.request.yaml`
- `postman/collections/ONEVO Organization Admin API/07. Organization - Company/List Legal Entities.request.yaml`
- `postman/collections/ONEVO Organization Admin API/07. Organization - Company/Update Legal Entity.request.yaml`

The now-empty `07. Organization - Company` directory was removed along with them (this tooling has no separate per-folder manifest file — folder identity is purely the directory itself, confirmed by checking `.resources/definition.yaml`, which contains no reference to folder names/ordering).

**Not changed in this task:** `postman/environments/New Environment.environment.yaml` still shows as modified in `git status`, but that change (`legal_entity_id: ''` added) is a leftover uncommitted edit from the prior Part 2D task, not something touched in this cleanup. No environment file edits were made in this session — see §4.

**Created:**
- `LEGAL_ENTITY_POSTMAN_STALE_FOLDER_CLEANUP_REPORT.md` (this file)

---

## 2. Why the Folder Was Removed

All four requests in `07. Organization - Company` were inspected before deletion. None matched any of the six real, exposed routes:

| Stale request | Stale target | Real route (never matched) |
|---|---|---|
| Create Legal Entity | `POST {{tenant_host}}/organization/legal-entities` | `POST {{base_url}}/api/v1/org/legal-entities` |
| Get Legal Entity | `GET {{tenant_host}}/organization/legal-entities/:id` | `GET {{base_url}}/api/v1/org/legal-entities/{{legal_entity_id}}/general-settings` |
| List Legal Entities | `GET {{tenant_host}}/organization/legal-entities` | `GET {{base_url}}/api/v1/org/legal-entities` |
| Update Legal Entity | `PATCH {{tenant_host}}/organization/legal-entities/:id` | `PUT {{base_url}}/api/v1/org/legal-entities/{{id}}/general-settings` |

Wrong host variable (`tenant_host` instead of `base_url`), wrong path (`/organization/legal-entities` instead of `/api/v1/org/legal-entities`), wrong verb on Update (`PATCH` vs `PUT`), and flat invented fields (`country`, `address` as a string) that never matched the real `CreateLegalEntityRequest`/`UpdateLegalEntityGeneralSettingsRequest` contracts at any point in Parts 2A–2D. Per the task's instruction ("delete ... if they do not exactly match the six valid Legal Entity routes"), all four were deleted — none partially matched, so no partial-folder correction was needed.

---

## 3. Confirmed: Valid Folder Preserved, No Duplicates Remain

`06. Organization - Companies` (added in Part 2D) was not touched. Its 10 request files are all still present:

```
06. Organization - Companies/
  Create Company - Duplicate Name Should 409.request.yaml
  Create Company.request.yaml
  Delete Company - Last Company Should Fail 400.request.yaml
  Delete Company - Wrong Confirm Name Should 400.request.yaml
  Delete Company.request.yaml
  Get Company General Settings.request.yaml
  Get Missing Company Should 404.request.yaml
  List Companies.request.yaml
  Remove Company Logo.request.yaml
  Update Company General Settings.request.yaml
```

After deletion, `06. Organization - Companies` is the **only** Legal Entity/Company folder left in the collection — confirmed by listing every remaining top-level folder:

```
03. Legal Acceptance
04. MFA
05. Password
06. Invitations
06. Organization - Companies
99. Health
```

(The two folders both numbered "06." — `06. Invitations` and `06. Organization - Companies` — is a pre-existing numbering quirk from Part 2D, not something this cleanup task was scoped to renumber; it does not affect routing or produce duplicate Company APIs.)

---

## 4. Search Results for Stale Routes (Post-Cleanup)

All searches run against the full collection directory (`postman/collections/ONEVO Organization Admin API`) after the deletion:

| Search | Result |
|---|---|
| `organization/company` | 0 matches |
| `companies/general` | 0 matches |
| `company-settings` | 0 matches |
| `PUT` request targeting `.../logo` | 0 matches — the collection's only `logo` reference is `Remove Company Logo.request.yaml`, confirmed `method: DELETE`; the collection's only `PUT` request is `Update Company General Settings.request.yaml`, targeting `.../general-settings` |
| Duplicate Legal Entity/Company folders | 0 — `06. Organization - Companies` is the sole remaining folder for this feature |

### Environment variables

No environment edits were made in this cleanup. Verified that removing the stale folder did not orphan any variable: the stale folder's only distinctive variable was `tenant_host`, which is still used by `03. Legal Acceptance`, `04. MFA`, `05. Password`, and `06. Invitations` (11 other request files reference it) — so it must stay and was left untouched. All five variables the task requires to be preserved are still present in `postman/environments/New Environment.environment.yaml`: `base_url`, `tenant_email`, `tenant_password`, `tenant_csrf_token`, `legal_entity_id`.

---

## 5. Validation

- **`git diff --check`** → exit code 0. One informational notice (pre-existing, unrelated): `LF will be replaced by CRLF` on `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs` — not a whitespace error, not touched by this task.
- **Postman format validation tooling:** none exists in this repo. The only Postman-adjacent tooling found is `.postman/resources.yaml`, a sync-extension pointer file, not a linter or schema validator. No `newman`, no Postman CLI config, no `package.json`-based lint script for these YAML files. Validation therefore relied on structural inspection: every deleted file and every remaining file in `06. Organization - Companies` was read and confirmed to follow the same `$kind: http-request` / `method` / `url` / `order` schema used throughout the rest of the collection (e.g. `03. Legal Acceptance`, `06. Invitations`), so no schema drift was introduced by this deletion (deleting files cannot introduce a schema error; only additions/edits could, and none were made to any request file).

---

## 6. Confirmation: No Backend Code, Tests, Migrations, or Docs Changed

Verified via `git status --porcelain`: the only changes attributable to this task are the four deletions under `postman/collections/ONEVO Organization Admin API/07. Organization - Company/`. All other entries in `git status` (Application/Domain/Infrastructure source changes, test file changes, the new migration, the four `LEGAL_ENTITY_GENERAL_SETTINGS_PART2*` reports, and the `postman/environments/New Environment.environment.yaml` modification) are pre-existing, uncommitted work from Parts 2A–2D and were not touched, re-edited, staged, or committed in this task.

No `git add`, `git commit`, or `git push` was run at any point.
