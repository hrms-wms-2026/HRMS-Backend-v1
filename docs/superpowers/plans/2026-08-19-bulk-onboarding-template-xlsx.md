# Bulk Onboarding Template & XLSX Upload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let HR download a CSV or XLSX starting template before their first bulk-onboarding upload, and accept `.xlsx` files (not just `.csv`) in the upload step.

**Architecture:** `UploadBulkOnboardingBatchCommand` moves from a text `CsvContent` field to raw `FileContent: byte[]`, with the handler branching on file extension between the existing `CsvBatchParser` and a new `XlsxBatchParser` (ClosedXML) — both returning the same shared record so nothing downstream changes. A new template endpoint generates both formats from one canonical field list. Frontend gets a wider file-type accept/validation and two new download links.

**Tech Stack:** .NET 10/EF Core backend on branch `local/reporting-manager-run` (`C:\onevoNew\HRMS-Backend-v1` — run all backend commands from there), Angular 21 frontend on branch `feature/employee-management-phase1-foundation` (`C:\onevoNew\Hrms--Web-application---front-end---v1`). New dependency: `ClosedXML` (MIT license, confirmed free for commercial use).

## Global Constraints

- `ClosedXML`, not `EPPlus` — EPPlus 5+ requires a paid commercial license (Polyform Noncommercial), ClosedXML is MIT and free.
- Template example row: real values only for fields with no tenant-specific meaning (First Name, Last Name, Work Email, Start Date, Employment Type as a fixed enum value). Every field whose value must match existing tenant data (Department, Position, Work Mode, Checklist Template, Reporting Manager) gets a **blank** example cell — never a plausible-looking fake value.
- The template endpoint's output is identical for every tenant (no tenant-specific data in it) — no `legalEntityId` parameter needed, just `format`.
- Both parsers (`CsvBatchParser`, `XlsxBatchParser`) return the same shared record type so `UploadBulkOnboardingBatchCommandHandler`'s downstream logic (batch/row creation, `ColumnMappingSuggester.Suggest`) needs no branching beyond the parse call itself.

---

## Task 1: Add ClosedXML dependency

**Files:**
- Modify: `src/ONEVO.Application/ONEVO.Application.csproj`

**Interfaces:**
- Produces: `ClosedXML.Excel` namespace available to `ONEVO.Application` — consumed by Task 3 (`XlsxBatchParser`) and Task 5 (template generation).

- [ ] **Step 1: Add the package reference**

Run from the backend repo root:
```bash
dotnet add src/ONEVO.Application/ONEVO.Application.csproj package ClosedXML
```

- [ ] **Step 2: Verify the build picks it up**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj`
Expected: succeeds, and `<PackageReference Include="ClosedXML" Version="..." />` now appears in the `.csproj`.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/ONEVO.Application.csproj
git commit -m "chore: add ClosedXML dependency for xlsx read/write"
```

---

## Task 2: Rename `ParsedCsv` to `ParsedBatchFile`

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/CsvBatchParser.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/UploadBulkOnboardingBatchCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/CsvBatchParserTests.cs` (find and update the existing file's type references — do not rewrite its test logic, only the type name)

**Interfaces:**
- Produces: `ParsedBatchFile(Headers: IReadOnlyList<string>, Rows: IReadOnlyList<IReadOnlyDictionary<string, string>>)` — replaces `ParsedCsv` everywhere. Consumed by Task 3 (`XlsxBatchParser` returns the same type) and Task 4 (handler).

- [ ] **Step 1: Run the existing CSV parser tests to establish a baseline**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CsvBatchParser"`
Expected: PASS (this task is a pure rename — nothing about parsing behavior changes, so these tests should pass before and after with only the type name updated).

- [ ] **Step 2: Rename the record and its one usage in `CsvBatchParser.cs`**

In `CsvBatchParser.cs`, rename:
```csharp
public sealed record ParsedBatchFile(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);
```
and change every `Result<ParsedCsv>` in the file to `Result<ParsedBatchFile>`, and the one `new ParsedCsv(headers, rows)` construction to `new ParsedBatchFile(headers, rows)`. No other logic in this file changes.

- [ ] **Step 3: Update the handler's reference**

In `UploadBulkOnboardingBatchCommandHandler.cs`, the existing `var parsed = CsvBatchParser.Parse(request.CsvContent);` line's inferred type changes automatically with the rename — no explicit type annotation to update there, but confirm via build.

- [ ] **Step 4: Update the existing test file's type references**

Find every `ParsedCsv` in `CsvBatchParserTests.cs` (or wherever the existing CSV parser tests live — locate via `grep -rln "ParsedCsv" tests/`) and replace with `ParsedBatchFile`. Test *logic* (inputs, assertions) is unchanged.

- [ ] **Step 5: Run the tests to verify they still pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CsvBatchParser"`
Expected: PASS

- [ ] **Step 6: Build the whole solution to catch any other reference**

Run: `dotnet build`
Expected: succeeds. Fix any other `ParsedCsv` reference the compiler flags (`grep -rln "ParsedCsv" src/` to find any missed).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/CsvBatchParser.cs src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/UploadBulkOnboardingBatchCommandHandler.cs tests/
git commit -m "refactor: rename ParsedCsv to ParsedBatchFile ahead of xlsx support"
```

---

## Task 3: `XlsxBatchParser`

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/XlsxBatchParser.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/XlsxBatchParserTests.cs`

**Interfaces:**
- Consumes: `ClosedXML.Excel` (Task 1), `ParsedBatchFile` (Task 2).
- Produces: `XlsxBatchParser.Parse(byte[] fileContent) -> Result<ParsedBatchFile>`. Consumed by Task 4 (upload handler).

- [ ] **Step 1: Write the failing tests**

```csharp
using ClosedXML.Excel;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public class XlsxBatchParserTests
{
    private static byte[] BuildWorkbook(string[] headers, IEnumerable<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        for (var col = 0; col < headers.Length; col++)
            sheet.Cell(1, col + 1).Value = headers[col];

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
                sheet.Cell(rowIndex, col + 1).Value = row[col];
            rowIndex++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void Parse_Reads_Headers_And_Rows_From_First_Sheet()
    {
        var bytes = BuildWorkbook(
            ["First Name", "Last Name", "Work Email"],
            [["Jane", "Doe", "jane@acme.test"], ["Bob", "Smith", "bob@acme.test"]]);

        var result = XlsxBatchParser.Parse(bytes);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Headers.Should().Equal("First Name", "Last Name", "Work Email");
        result.Value.Rows.Should().HaveCount(2);
        result.Value.Rows[0]["First Name"].Should().Be("Jane");
        result.Value.Rows[1]["Work Email"].Should().Be("bob@acme.test");
    }

    [Fact]
    public void Parse_Fails_On_Header_Only_Workbook()
    {
        var bytes = BuildWorkbook(["First Name", "Last Name"], []);

        var result = XlsxBatchParser.Parse(bytes);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no data rows");
    }

    [Fact]
    public void Parse_Fails_When_Row_Count_Exceeds_MaxRows()
    {
        var tooManyRows = Enumerable.Range(1, CsvBatchParser.MaxRows + 1)
            .Select(i => new[] { $"First{i}", "Doe", $"person{i}@acme.test" });
        var bytes = BuildWorkbook(["First Name", "Last Name", "Work Email"], tooManyRows);

        var result = XlsxBatchParser.Parse(bytes);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain(CsvBatchParser.MaxRows.ToString());
    }

    [Fact]
    public void Parse_Fails_On_Corrupt_Bytes()
    {
        var result = XlsxBatchParser.Parse([1, 2, 3, 4, 5]);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Parse_Reads_Date_Cells_As_Human_Readable_Text_Not_Serial_Numbers()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell(1, 1).Value = "Start Date";
        sheet.Cell(2, 1).Value = new DateTime(2026, 9, 1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var result = XlsxBatchParser.Parse(stream.ToArray());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows[0]["Start Date"].Should().NotMatchRegex(@"^\d+(\.\d+)?$"); // not a raw OLE serial number
        result.Value.Rows[0]["Start Date"].Should().Contain("2026");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~XlsxBatchParserTests"`
Expected: FAIL — `XlsxBatchParser` doesn't exist yet.

- [ ] **Step 3: Implement the parser**

```csharp
using ClosedXML.Excel;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

/// <summary>
/// Reads the first worksheet of an uploaded .xlsx workbook into the same ParsedBatchFile shape
/// CsvBatchParser produces, so UploadBulkOnboardingBatchCommandHandler doesn't branch beyond the
/// parse call itself. Cell values are read via GetString() (ClosedXML's "what a human sees" text
/// representation, respecting the cell's number/date format) rather than the raw typed .Value, so
/// a date cell round-trips as e.g. "2026-09-01" instead of an OLE automation serial number.
/// </summary>
public static class XlsxBatchParser
{
    public static Result<ParsedBatchFile> Parse(byte[] fileContent)
    {
        IXLWorksheet sheet;
        try
        {
            using var stream = new MemoryStream(fileContent);
            using var workbook = new XLWorkbook(stream);
            sheet = workbook.Worksheets.First();

            var usedRange = sheet.RangeUsed();
            if (usedRange is null)
                return Result<ParsedBatchFile>.Failure("The file is empty.");

            var firstRow = usedRange.FirstRow();
            var headers = firstRow.Cells()
                .Select(c => c.GetString().Trim())
                .Where(h => h.Length > 0)
                .ToList();

            if (headers.Count == 0)
                return Result<ParsedBatchFile>.Failure("The file is empty.");

            var dataRows = usedRange.Rows()
                .Skip(1)
                .Where(r => r.Cells().Any(c => !string.IsNullOrWhiteSpace(c.GetString())))
                .ToList();

            if (dataRows.Count == 0)
                return Result<ParsedBatchFile>.Failure("The file has a header row but no data rows.");

            if (dataRows.Count > CsvBatchParser.MaxRows)
                return Result<ParsedBatchFile>.Failure(
                    $"This file has {dataRows.Count} rows; the limit is {CsvBatchParser.MaxRows} rows per upload.");

            var rows = new List<IReadOnlyDictionary<string, string>>();
            foreach (var dataRow in dataRows)
            {
                var row = new Dictionary<string, string>();
                for (var i = 0; i < headers.Count; i++)
                {
                    var cell = dataRow.Cell(i + 1);
                    row[headers[i]] = cell.GetString().Trim();
                }
                rows.Add(row);
            }

            return Result<ParsedBatchFile>.Success(new ParsedBatchFile(headers, rows));
        }
        catch (Exception)
        {
            return Result<ParsedBatchFile>.Failure("Could not read this file as an Excel workbook.");
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~XlsxBatchParserTests"`
Expected: PASS. If the date-cell test fails because `GetString()` returns something other than a string containing "2026" (ClosedXML's default date format may render differently, e.g. `09/01/2026`), adjust the test's assertion to check for the actual rendered format rather than assuming ISO — the important property being tested is "not a bare serial number," not a specific format string.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/XlsxBatchParser.cs tests/
git commit -m "feat: add XlsxBatchParser for .xlsx bulk-onboarding uploads"
```

---

## Task 4: Upload path accepts `.xlsx`, branches on extension

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/UploadBulkOnboardingBatchCommand.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/UploadBulkOnboardingBatchCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingUploadTests.cs`

**Interfaces:**
- Consumes: `XlsxBatchParser.Parse` (Task 3), `CsvBatchParser.Parse` (unchanged).
- Produces: `UploadBulkOnboardingBatchCommand.FileContent: byte[]` (replaces `CsvContent: string`).

- [ ] **Step 1: Write the failing integration test**

Add to `BulkOnboardingUploadTests.cs`, mirroring its existing end-to-end upload test's HTTP client / multipart setup:

```csharp
[Fact]
public async Task Upload_Accepts_Xlsx_File_And_Detects_Columns()
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.Worksheets.Add("Sheet1");
    sheet.Cell(1, 1).Value = "First Name";
    sheet.Cell(1, 2).Value = "Work Email";
    sheet.Cell(2, 1).Value = "Jane";
    sheet.Cell(2, 2).Value = "jane@acme.test";
    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    stream.Position = 0;

    using var content = new MultipartFormDataContent();
    content.Add(new StreamContent(stream), "file", "roster.xlsx");
    content.Add(new StringContent(_legalEntityId.ToString()), "legalEntityId");

    var response = await _client.PostAsync("/api/v1/onboarding/bulk-batches", content);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<BulkOnboardingBatchViewModel>();
    body!.DetectedColumns.Should().Contain("First Name");
    body.DetectedColumns.Should().Contain("Work Email");
}

[Fact]
public async Task Upload_Rejects_Unsupported_File_Extension()
{
    using var content = new MultipartFormDataContent();
    content.Add(new StringContent("not a real file"), "file"); // no filename → server-side extension check should still reject a .txt-style upload
    content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("data")), "file");
    content.Add(new StringContent(_legalEntityId.ToString()), "legalEntityId");

    // Simpler: build the multipart with an explicit unsupported filename instead of the ambiguous form above.
    using var content2 = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("First Name\nJane"));
    content2.Add(fileContent, "file", "roster.txt");
    content2.Add(new StringContent(_legalEntityId.ToString()), "legalEntityId");

    var response = await _client.PostAsync("/api/v1/onboarding/bulk-batches", content2);

    response.IsSuccessStatusCode.Should().BeFalse();
}
```

(Remove the first, ambiguous `content` block in the rejection test — it was scratch reasoning; keep only `content2`'s clean version. Match `_client`/`_legalEntityId` to whatever the existing test class's fields are actually named.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~Upload_Accepts_Xlsx_File_And_Detects_Columns"`
Expected: FAIL — `.xlsx` upload currently gets read as text via `StreamReader` and fails CSV parsing (binary bytes aren't valid CSV text).

- [ ] **Step 3: Update the command**

```csharp
public sealed record UploadBulkOnboardingBatchCommand(
    string OriginalFileName,
    byte[] FileContent,
    Guid LegalEntityId,
    int? DefaultWorkModeId,
    string? DefaultEmploymentType,
    Guid? DefaultChecklistTemplateId) : IRequest<Result<BulkOnboardingBatchResponse>>;
```

- [ ] **Step 4: Branch in the handler**

In `UploadBulkOnboardingBatchCommandHandler.Handle`, replace the single `CsvBatchParser.Parse(request.CsvContent)` call with:

```csharp
var extension = Path.GetExtension(request.OriginalFileName).ToLowerInvariant();
Result<ParsedBatchFile> parsed = extension switch
{
    ".csv" => CsvBatchParser.Parse(System.Text.Encoding.UTF8.GetString(request.FileContent)),
    ".xlsx" => XlsxBatchParser.Parse(request.FileContent),
    _ => Result<ParsedBatchFile>.Failure("Upload a .csv or .xlsx file."),
};

if (!parsed.IsSuccess)
    return Result<BulkOnboardingBatchResponse>.Failure(parsed.Error!);
```

(`parsed.Value!` usage further down the method — already present, referencing `.Rows`/`.Headers` — needs no change since `ParsedBatchFile` has the same shape as the old `ParsedCsv`.)

- [ ] **Step 5: Update the controller to read bytes, not text**

In `BulkOnboardingController.Upload`:

```csharp
[HttpPost]
[RequirePermission("employees:write")]
public async Task<IActionResult> Upload([FromForm] UploadBulkOnboardingBatchRequest request, CancellationToken ct = default)
{
    using var memoryStream = new MemoryStream();
    await request.File.CopyToAsync(memoryStream, ct);
    var fileContent = memoryStream.ToArray();

    var command = new UploadBulkOnboardingBatchCommand(
        request.File.FileName, fileContent, request.LegalEntityId,
        request.DefaultWorkModeId, request.DefaultEmploymentType, request.DefaultChecklistTemplateId);

    var result = await _mediator.Send(command, ct);
    if (!result.IsSuccess)
        return Problem(result.Error, statusCode: result.StatusCode ?? 400);

    var response = result.Value!;
    return Ok(new BulkOnboardingBatchViewModel(
        response.Id, response.Status, response.TotalRows, response.ValidRows, response.InvalidRows,
        response.DetectedColumns, response.SuggestedMapping));
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~BulkOnboardingUploadTests"`
Expected: PASS — including the pre-existing CSV upload tests in this file, which must keep passing unmodified (CSV path behavior is unchanged, just reached via bytes→UTF8-decode now instead of the controller decoding to text directly).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/ src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs tests/
git commit -m "feat: accept .xlsx uploads in bulk onboarding alongside .csv"
```

---

## Task 5: Template download endpoint (CSV + XLSX)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/BulkOnboardingTemplateFields.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Queries/GetBulkOnboardingTemplate/GetBulkOnboardingTemplateQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Queries/GetBulkOnboardingTemplate/GetBulkOnboardingTemplateQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/GetBulkOnboardingTemplateQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ClosedXML.Excel` (Task 1).
- Produces: `GET api/v1/onboarding/bulk-batches/template?format=csv|xlsx`. Consumed by Task 6 (frontend).

- [ ] **Step 1: Write the failing tests**

```csharp
public class GetBulkOnboardingTemplateQueryHandlerTests
{
    private readonly GetBulkOnboardingTemplateQueryHandler _handler = new();

    [Fact]
    public void Handle_Csv_Includes_All_Field_Labels_In_Order()
    {
        var result = _handler.Handle(new GetBulkOnboardingTemplateQuery("csv"));

        result.IsSuccess.Should().BeTrue();
        var text = System.Text.Encoding.UTF8.GetString(result.Value!.Content);
        var firstLine = text.Split('\n')[0].TrimEnd('\r');
        firstLine.Should().Be("First Name,Last Name,Work Email,Start Date,Employment Type,Work Mode,Department,Position,Checklist Template,Employee Number,Reporting Manager");
    }

    [Fact]
    public void Handle_Csv_Leaves_Tenant_Specific_Fields_Blank_In_Example_Row()
    {
        var result = _handler.Handle(new GetBulkOnboardingTemplateQuery("csv"));

        var text = System.Text.Encoding.UTF8.GetString(result.Value!.Content);
        var lines = text.Split('\n');
        var exampleRow = lines[1].TrimEnd('\r').Split(',');
        exampleRow[0].Should().Be("Jane"); // First Name - safe example
        exampleRow[5].Should().BeEmpty(); // Work Mode - tenant-specific, blank
        exampleRow[6].Should().BeEmpty(); // Department - tenant-specific, blank
        exampleRow[10].Should().BeEmpty(); // Reporting Manager - tenant-specific, blank
    }

    [Fact]
    public void Handle_Xlsx_Produces_A_Readable_Workbook_With_The_Same_Headers()
    {
        var result = _handler.Handle(new GetBulkOnboardingTemplateQuery("xlsx"));

        result.IsSuccess.Should().BeTrue();
        using var stream = new MemoryStream(result.Value!.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        sheet.Cell(1, 1).GetString().Should().Be("First Name");
        sheet.Cell(1, 11).GetString().Should().Be("Reporting Manager");
        sheet.Cell(2, 1).GetString().Should().Be("Jane");
    }

    [Fact]
    public void Handle_Returns_Failure_For_Unsupported_Format()
    {
        var result = _handler.Handle(new GetBulkOnboardingTemplateQuery("pdf"));

        result.IsSuccess.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetBulkOnboardingTemplateQueryHandlerTests"`
Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Add the canonical field list**

```csharp
namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public sealed record BulkOnboardingTemplateField(string FieldKey, string Label, string ExampleValue);

/// <summary>
/// Same field keys and order as ColumnMappingSuggester.FieldAliases. ExampleValue is blank for
/// every field whose real value must match existing tenant data (Department/Position/Work
/// Mode/Checklist Template names, a Reporting Manager's email) - a plausible-looking fake value
/// there would read as something the system literally expects. Only universal, tenant-independent
/// fields get a real example.
/// </summary>
public static class BulkOnboardingTemplateFields
{
    public static readonly IReadOnlyList<BulkOnboardingTemplateField> All =
    [
        new("firstName", "First Name", "Jane"),
        new("lastName", "Last Name", "Doe"),
        new("workEmail", "Work Email", "jane.doe@example.com"),
        new("startDate", "Start Date", "2026-09-01"),
        new("employmentType", "Employment Type", "full_time"),
        new("workMode", "Work Mode", ""),
        new("department", "Department", ""),
        new("position", "Position", ""),
        new("checklistTemplate", "Checklist Template", ""),
        new("employeeNumber", "Employee Number", ""),
        new("reportingManager", "Reporting Manager", ""),
    ];
}
```

- [ ] **Step 4: Add the query and handler**

```csharp
public sealed record GetBulkOnboardingTemplateQuery(string Format);

public sealed record BulkOnboardingTemplateFile(byte[] Content, string ContentType, string FileName);
```

```csharp
using ClosedXML.Excel;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingTemplate;

public sealed class GetBulkOnboardingTemplateQueryHandler
{
    public Result<BulkOnboardingTemplateFile> Handle(GetBulkOnboardingTemplateQuery request)
    {
        return request.Format.ToLowerInvariant() switch
        {
            "csv" => Result<BulkOnboardingTemplateFile>.Success(BuildCsv()),
            "xlsx" => Result<BulkOnboardingTemplateFile>.Success(BuildXlsx()),
            _ => Result<BulkOnboardingTemplateFile>.Failure("Unsupported template format. Use 'csv' or 'xlsx'."),
        };
    }

    private static BulkOnboardingTemplateFile BuildCsv()
    {
        var fields = BulkOnboardingTemplateFields.All;
        var header = string.Join(",", fields.Select(f => f.Label));
        var exampleRow = string.Join(",", fields.Select(f => f.ExampleValue));
        var content = System.Text.Encoding.UTF8.GetBytes($"{header}\n{exampleRow}\n");

        return new BulkOnboardingTemplateFile(content, "text/csv", "bulk-onboarding-template.csv");
    }

    private static BulkOnboardingTemplateFile BuildXlsx()
    {
        var fields = BulkOnboardingTemplateFields.All;
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Template");

        for (var i = 0; i < fields.Count; i++)
            sheet.Cell(1, i + 1).Value = fields[i].Label;

        for (var i = 0; i < fields.Count; i++)
            sheet.Cell(2, i + 1).Value = fields[i].ExampleValue;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return new BulkOnboardingTemplateFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "bulk-onboarding-template.xlsx");
    }
}
```

(This handler is a plain class, not an `IRequestHandler<,>` via MediatR — it has no tenant/auth-dependent logic, no repository calls, and produces byte content, not a `Result<T>`-wrapped domain response, so it's invoked directly from the controller rather than through `_mediator.Send`. This matches the design's §4.3 observation that the template is identical for every tenant.)

- [ ] **Step 5: Add the controller endpoint**

In `BulkOnboardingController.cs`, add the handler as a constructor dependency and a new action:

```csharp
private readonly GetBulkOnboardingTemplateQueryHandler _templateHandler;

public BulkOnboardingController(IMediator mediator, GetBulkOnboardingTemplateQueryHandler templateHandler)
{
    _mediator = mediator;
    _templateHandler = templateHandler;
}

[HttpGet("template")]
[RequirePermission("employees:write")]
public IActionResult GetTemplate([FromQuery] string format)
{
    var result = _templateHandler.Handle(new GetBulkOnboardingTemplateQuery(format));
    if (!result.IsSuccess)
        return Problem(result.Error, statusCode: result.StatusCode ?? 400);

    var file = result.Value!;
    return File(file.Content, file.ContentType, file.FileName);
}
```

Register `GetBulkOnboardingTemplateQueryHandler` in `DependencyInjection.cs` if this codebase requires explicit registration for plain (non-MediatR) classes — check how other directly-injected, non-`IRequestHandler` services are registered in that file and follow the same pattern (likely `services.AddScoped<GetBulkOnboardingTemplateQueryHandler>();` or `AddTransient`, since it holds no state).

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetBulkOnboardingTemplateQueryHandlerTests"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/BulkOnboardingTemplateFields.cs src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Queries/GetBulkOnboardingTemplate/ src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/
git commit -m "feat: add bulk onboarding template download endpoint (CSV + XLSX)"
```

---

## Task 6: Frontend — download template API method

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/data-access/bulk-onboarding-api.service.ts`
- Test: `bulk-onboarding-api.service.spec.ts`

**Interfaces:**
- Produces: `BulkOnboardingApiService.downloadTemplate(format: 'csv' | 'xlsx'): Observable<Blob>`. Consumed by Task 7.

- [ ] **Step 1: Write the failing test**

```typescript
it('downloadTemplate requests the template endpoint as a blob with the given format', () => {
  service.downloadTemplate('xlsx').subscribe();

  const req = httpMock.expectOne((r) => r.url === `${baseUrl}/template` && r.params.get('format') === 'xlsx');
  expect(req.request.method).toBe('GET');
  expect(req.request.responseType).toBe('blob');
  req.flush(new Blob());
});
```

(Match `baseUrl` to whatever constant the existing spec file already uses — likely the same `${environment.apiUrl}/onboarding/bulk-batches` the service itself defines.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx ng test --include='**/bulk-onboarding-api.service.spec.ts'`
Expected: FAIL — method doesn't exist.

- [ ] **Step 3: Add the method**

In `bulk-onboarding-api.service.ts`, add the `HttpParams` import if not already present, and:

```typescript
downloadTemplate(format: 'csv' | 'xlsx'): Observable<Blob> {
  return this.http.get(`${this.baseUrl}/template`, {
    params: new HttpParams().set('format', format),
    responseType: 'blob'
  });
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx ng test --include='**/bulk-onboarding-api.service.spec.ts'`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/people/data-access/bulk-onboarding-api.service.ts src/app/modules/people/data-access/bulk-onboarding-api.service.spec.ts
git commit -m "feat: add downloadTemplate API method"
```

---

## Task 7: Frontend — accept `.xlsx`, add download-template links

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/feature/bulk-onboarding/bulk-onboarding.component.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/feature/bulk-onboarding/bulk-onboarding.component.html`
- Test: `bulk-onboarding.component.spec.ts`

**Interfaces:**
- Consumes: `BulkOnboardingApiService.downloadTemplate` (Task 6).

- [ ] **Step 1: Write the failing tests**

```typescript
it('accepts a .xlsx file without rejecting it', async () => {
  const file = new File(['dummy'], 'roster.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  const event = { target: { files: [file] } } as unknown as Event;

  await component.onFileSelected(event, 'legal-entity-1', null, null);

  expect(component.fileError()).toBeNull();
});

it('still rejects an unsupported file type with an updated message', async () => {
  const file = new File(['dummy'], 'roster.txt', { type: 'text/plain' });
  const event = { target: { files: [file] } } as unknown as Event;

  await component.onFileSelected(event, 'legal-entity-1', null, null);

  expect(component.fileError()).toContain('CSV or Excel');
});

it('triggers a template download when the CSV template link is clicked', () => {
  const blob = new Blob(['a,b,c'], { type: 'text/csv' });
  apiSpy.downloadTemplate.and.returnValue(of(blob));
  const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

  component.onDownloadTemplate('csv');

  expect(apiSpy.downloadTemplate).toHaveBeenCalledWith('csv');
  expect(clickSpy).toHaveBeenCalled();
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npx ng test --include='**/bulk-onboarding.component.spec.ts'`
Expected: FAIL — `.xlsx` currently rejected, `onDownloadTemplate` doesn't exist.

- [ ] **Step 3: Update the file-type validation**

In `bulk-onboarding.component.ts`'s `onFileSelected`:

```typescript
const isSupported =
  file.name.toLowerCase().endsWith('.csv') ||
  file.name.toLowerCase().endsWith('.xlsx');
if (!isSupported) {
  this.fileError.set('Upload a CSV or Excel (.xlsx) file.');
  input.value = '';
  this.selectedFileName.set(null);
  return;
}
```

- [ ] **Step 4: Add the download handler**

```typescript
private readonly bulkOnboardingApi = inject(BulkOnboardingApiService);

onDownloadTemplate(format: 'csv' | 'xlsx'): void {
  this.bulkOnboardingApi.downloadTemplate(format).subscribe((blob) => {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = format === 'csv' ? 'bulk-onboarding-template.csv' : 'bulk-onboarding-template.xlsx';
    link.click();
    URL.revokeObjectURL(url);
  });
}
```

(If `bulkOnboardingApi` is already injected under a different property name elsewhere in this component — check before adding a duplicate injection.)

- [ ] **Step 5: Update the template**

In `bulk-onboarding.component.html`'s Upload step, replace the "CSV only." copy and add the download links, and widen the file input's `accept`:

```html
<p class="bo-section__copy">
  CSV or Excel (.xlsx). Optional work mode and employment type defaults apply when a row has no mapped column for that field.
</p>

<div class="bo-template-links">
  <button type="button" class="bo-text-btn" (click)="onDownloadTemplate('csv')">Download CSV template</button>
  <button type="button" class="bo-text-btn" (click)="onDownloadTemplate('xlsx')">Download Excel template</button>
</div>
```

```html
<input
  type="file"
  accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
  (change)="onCsvInput($event)"
  [disabled]="store.loading()"
/>
```

Update the dropzone hint text ("Or click to choose a .csv file.") to "Or click to choose a .csv or .xlsx file."

- [ ] **Step 6: Run the tests to verify they pass**

Run: `npx ng test --include='**/bulk-onboarding.component.spec.ts'`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/app/modules/people/feature/bulk-onboarding/
git commit -m "feat: accept .xlsx uploads and add template download links to bulk onboarding"
```

---

## Self-Review Notes

- **Spec coverage**: §4.1 (upload branching) → Tasks 2, 4. §4.2 (`XlsxBatchParser`) → Task 3. §4.3 (template endpoint, blank-vs-filled example fields) → Task 5. §4.4 (ClosedXML dependency) → Task 1. §5 (frontend) → Tasks 6, 7. §7's open item (date-cell reading via `GetString()`) resolved concretely in Task 3 rather than left open, with the test written to check the *property* (not a raw serial number) rather than an assumed exact format string, since ClosedXML's default date rendering wasn't verified against a live install before writing this plan.
- **Placeholder scan**: none found — every step has real code.
- **Type consistency**: `ParsedBatchFile` (Task 2) used identically by both `CsvBatchParser` and `XlsxBatchParser` (Task 3) and the handler (Task 4). `BulkOnboardingTemplateField`/`BulkOnboardingTemplateFields.All` (Task 5) match `ColumnMappingSuggester.FieldAliases`'s exact key order, verified against the actual current file content, not assumed.
- **Note for whoever executes this**: Task 5's `GetBulkOnboardingTemplateQueryHandler` is deliberately *not* a MediatR `IRequestHandler` — it has no auth/tenant dependency and returns the same bytes for every caller, so routing it through `_mediator.Send` would add a pipeline behavior chain (permission/logging behaviors, if this codebase has any) for no benefit. If the codebase's MediatR pipeline behaviors are actually required for consistency (e.g. request logging), reconsider and make it a proper `IRequestHandler<GetBulkOnboardingTemplateQuery, Result<BulkOnboardingTemplateFile>>` instead — check `DependencyInjection.cs` and any global pipeline behaviors before committing to the plain-class shape written here.
