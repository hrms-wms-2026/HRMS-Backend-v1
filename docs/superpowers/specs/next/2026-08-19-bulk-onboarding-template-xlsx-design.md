# Bulk Onboarding — Template Download & XLSX Upload Design

**Status:** Approved by user 2026-08-19, ready for implementation planning.

**Origin:** brainstormed live with the user 2026-08-19 via `superpowers:brainstorming`. This is "Spec 1" from the original bulk-onboarding UX questions at the start of this session (reporting manager, template download, CSV-vs-Excel) — deferred while the reporting-manager-resolution and coverage-responsible-person work were designed and planned, now picked up on its own.

**Companion context:** builds on the existing bulk-onboarding backend (`UploadBulkOnboardingBatchCommand`, `CsvBatchParser`, `ColumnMappingSuggester` — all already implemented, verified present on the current branch) and frontend (`bulk-onboarding.component.ts`'s Upload step, currently CSV-only with an explicit rejection message for other file types).

---

## 1. Goal

Two independent UX gaps in the bulk-onboarding Upload step, closed together since both touch the same screen and the same underlying file-handling code: (1) HR has to guess the expected CSV column headers before their first upload — no template exists; (2) only `.csv` is accepted, even though `.xlsx` is a completely free format to support (ClosedXML, MIT-licensed, confirmed via web search during this session's earlier discussion — not the paid EPPlus 5+ the original bulk-onboarding design doc conflated it with).

## 2. Scope

**In scope:** `.xlsx` upload parsing (ClosedXML), a template-download endpoint producing both CSV and XLSX, and the frontend changes for both (file input accept/validation, two download links).

**Out of scope:** any change to column mapping, row validation, draft creation, or finalize — this only touches how the initial file gets in and what a blank starting file looks like. The 200-row cap, the mapping-suggestion algorithm, and everything downstream of "rows parsed into `ParsedBatchFile`" are untouched.

## 3. Current-state facts this design depends on

Verified directly against the codebase (current branch, `local/reporting-manager-run`):

- `BulkOnboardingController.Upload` (`src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs`) reads the uploaded `IFormFile` via `StreamReader` into a plain string, passed as `UploadBulkOnboardingBatchCommand.CsvContent`. This only works for text formats — `.xlsx` is a binary ZIP-based format and cannot go through this path unchanged.
- `CsvBatchParser.Parse(string)` (`src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/CsvBatchParser.cs`) returns `Result<ParsedCsv>`, where `ParsedCsv(Headers: IReadOnlyList<string>, Rows: IReadOnlyList<IReadOnlyDictionary<string, string>>)`. Enforces the 200-row cap (`CsvBatchParser.MaxRows`) and rejects empty files / header-only files.
- `ColumnMappingSuggester.FieldAliases` (`src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/ColumnMappingSuggester.cs`) is the canonical ordered list of system field keys: `firstName, lastName, workEmail, startDate, employmentType, workMode, department, position, checklistTemplate, employeeNumber, reportingManager`. No canonical *label* per key exists server-side today (only alias arrays used for auto-suggest matching) — the template generator needs one, introduced by this design (§5).
- No Excel library (`ClosedXML`, `EPPlus`, or `ExcelDataReader`) is referenced anywhere in the solution today (confirmed via project-wide `.csproj` search).
- Frontend `bulk-onboarding.component.ts`'s `onFileSelected` explicitly rejects anything that isn't `.csv`/`text/csv` with the message "Upload a CSV file. Spreadsheet formats are not accepted in this step." The dropzone's `accept` attribute is `.csv,text/csv`. No template-download UI exists on the Upload step.

## 4. Backend design

### 4.1 Upload path — one command, branch on extension

`UploadBulkOnboardingBatchCommand.CsvContent: string` → `FileContent: byte[]`. `BulkOnboardingController.Upload` reads the `IFormFile` into a `byte[]` (via `CopyToAsync` into a `MemoryStream`) unconditionally — no more `StreamReader`.

`UploadBulkOnboardingBatchCommandHandler` branches on `Path.GetExtension(request.OriginalFileName)`:
- `.csv` → `Encoding.UTF8.GetString(request.FileContent)` → `CsvBatchParser.Parse(text)` (unchanged).
- `.xlsx` → new `XlsxBatchParser.Parse(request.FileContent)`.
- anything else → `Result<...>.Failure("Upload a .csv or .xlsx file.")`, mirroring the frontend's existing client-side check as defense in depth.

Both parsers return the same record, renamed from `ParsedCsv` to the neutral `ParsedBatchFile` (one type, one rename, one call site update — `CsvBatchParser`'s signature changes to `Result<ParsedBatchFile>`) so the rest of the handler (batch/row creation, `ColumnMappingSuggester.Suggest`) needs no branching beyond the parse step itself.

### 4.2 `XlsxBatchParser`

New file alongside `CsvBatchParser.cs`, using `ClosedXML.Excel`: opens the workbook from a `MemoryStream` over the byte array, reads the first worksheet, first row = headers (cell text, trimmed), subsequent non-empty rows = data (cell values coerced to string — dates read via `.GetString()` after ensuring the workbook cell's formatted/display value is used, not a raw OLE date serial, so a date typed into Excel round-trips as the same text a human would expect). Same `MaxRows` cap and same empty/header-only-file rejections as `CsvBatchParser`, reusing `CsvBatchParser.MaxRows` rather than a second constant.

### 4.3 Template generation endpoint

New: `GET api/v1/onboarding/bulk-batches/template?format=csv|xlsx`, `[RequirePermission("employees:write")]` (matches Upload's permission — this is part of the same upload flow). A new `BulkOnboardingTemplateFields` static list (same file area as `ColumnMappingSuggester`) provides `(FieldKey, Label, ExampleValue)` in `ColumnMappingSuggester.FieldAliases`' exact key order:

| FieldKey | Label | ExampleValue |
|---|---|---|
| firstName | First Name | Jane |
| lastName | Last Name | Doe |
| workEmail | Work Email | jane.doe@example.com |
| startDate | Start Date | 2026-09-01 |
| employmentType | Employment Type | full_time |
| workMode | Work Mode | *(blank)* |
| department | Department | *(blank)* |
| position | Position | *(blank)* |
| checklistTemplate | Checklist Template | *(blank)* |
| employeeNumber | Employee Number | *(blank)* |
| reportingManager | Reporting Manager | *(blank)* |

Blank for anything that must match a tenant's actual configured data (Department/Position/Work Mode/Checklist Template names, or a Reporting Manager's email) — a fake example there would look like a value the system expects literally, exactly the risk flagged when this design was discussed. `employmentType`'s example is safe because it's a fixed enum (`full_time`/`part_time`/`contractor`/`intern`), not tenant data.

`format=csv` → builds the two-line CSV in-memory (header row + example row), returned as `text/csv`, filename `bulk-onboarding-template.csv`. `format=xlsx` → same two rows written via ClosedXML (`IXLWorksheet.Cell(row, col).Value = ...`), returned as `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, filename `bulk-onboarding-template.xlsx`. Both are `FileContentResult`s — no persistence, no batch created, nothing tenant-specific in the response (identical output for every tenant, so no auth-beyond-permission-check logic needed beyond the existing `[RequirePermission]`).

### 4.4 Dependency

Add `ClosedXML` (MIT license, confirmed free for commercial use) as a NuGet package reference on `ONEVO.Application` — same project `CsvBatchParser`/`XlsxBatchParser` already live in, keeping both parsers and both template writers in one place rather than splitting parsing (Application) from writing (a different layer) for no reason.

## 5. Frontend design

- `bulk-onboarding.component.ts`'s `onFileSelected`: extension check becomes `file.name.toLowerCase().endsWith('.csv') || file.name.toLowerCase().endsWith('.xlsx')`; rejection message becomes "Upload a CSV or Excel (.xlsx) file."
- Dropzone `accept` attribute: `.csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
- Upload step copy: "CSV only." → "CSV or Excel (.xlsx)."
- Two new "Download template" links (CSV / XLSX) on the Upload step, above or beside the dropzone. New `BulkOnboardingApiService` method `downloadTemplate(format: 'csv' | 'xlsx')` — `responseType: 'blob'` HTTP GET to the new endpoint, then the standard browser blob-download pattern (object URL + temporary `<a download>` click, revoked after).
- No store/state change needed — this is a fire-and-forget download action, not part of the upload/validate/finalize state machine.

## 6. Testing

- **Unit**: `XlsxBatchParser` — happy path (headers + rows), empty workbook, header-only, over-cap, non-`.xlsx` bytes (corrupt file) → failure result. Template-generation handler — correct header order/labels for both formats, blank cells for tenant-specific fields, example values present for the safe fields.
- **Integration**: `POST .../bulk-batches` with a real `.xlsx` file (built via ClosedXML in the test itself, round-tripping the same library) produces the same `BulkOnboardingBatchResponse` shape as an equivalent `.csv` upload — same detected columns, same suggested mapping. `GET .../bulk-batches/template?format=xlsx` and `?format=csv` return well-formed files matching the field table in §4.3.
- **Frontend**: file-input validation accepts `.xlsx`, rejects other extensions with the updated message; download-template method requests the right URL/format and triggers a blob download (spy-based, no real file-system write in the test).

## 7. Open items for the plan to resolve

- Exact ClosedXML API calls for reading a cell's "what a human sees" string value for date-typed cells (`IXLCell.GetString()` vs `.GetDateTime().ToString(...)` vs checking `DataType`) — implementation detail, verify against ClosedXML's actual API surface when writing the parser, not guessed here.
- Whether the template endpoint needs a `legalEntityId` route/query parameter at all, given its output is identical for every tenant/legal entity (no per-tenant data in the template) — leaning no (keep it parameterless beyond `format`), but the plan should confirm this against the existing `BulkOnboardingController`'s route base (`api/v1/onboarding/bulk-batches`, no legal-entity segment) before finalizing the exact route.
