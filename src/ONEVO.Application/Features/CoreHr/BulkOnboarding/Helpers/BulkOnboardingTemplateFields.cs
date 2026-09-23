namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public sealed record BulkOnboardingTemplateField(string FieldKey, string Label, string ExampleValue);

/// <summary>
/// Same field keys and order as ColumnMappingSuggester.FieldAliases. ExampleValue is blank for
/// every field whose real value must match existing tenant data.
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
