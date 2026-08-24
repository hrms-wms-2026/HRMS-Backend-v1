namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;

public static class BulkOnboardingIssueTypes
{
    public const string DepartmentNotFound = "department_not_found";
    public const string PositionNotFound = "position_not_found";
    public const string WorkModeMissing = "work_mode_missing";
    public const string WorkModeNotFound = "work_mode_not_found";
    public const string EmploymentTypeNotFound = "employment_type_not_found";
    public const string EmploymentTypeMissing = "employment_type_missing";
    public const string ChecklistTemplateNotFound = "checklist_template_not_found";
    public const string DuplicateWorkEmail = "duplicate_work_email";
    public const string DuplicateEmployeeNumber = "duplicate_employee_number";
    public const string InvalidStartDate = "invalid_start_date";
    public const string MissingFirstName = "missing_first_name";
    public const string MissingLastName = "missing_last_name";
    public const string MissingWorkEmail = "missing_work_email";
    public const string ReportingManagerRequired = "reporting_manager_required";
    public const string ReportingManagerNotFound = "reporting_manager_not_found";
    public const string PositionCapacityExceeded = "position_capacity_exceeded";

    public static class Actions
    {
        public const string MapExisting = "map_existing";
        public const string EditImportedValue = "edit_imported_value";
        public const string CreateDepartment = "create_department";
        public const string CreatePosition = "create_position";
        public const string SetDefault = "set_default";
        public const string IncreaseCapacity = "increase_capacity";
    }

    public static bool IsSetupFixable(string issueType) => issueType is
        DepartmentNotFound or PositionNotFound or WorkModeMissing or WorkModeNotFound
        or EmploymentTypeNotFound or EmploymentTypeMissing or ChecklistTemplateNotFound
        or PositionCapacityExceeded;

    public static bool IsRowEdit(string issueType) => issueType is
        DuplicateWorkEmail or DuplicateEmployeeNumber or InvalidStartDate
        or MissingFirstName or MissingLastName or MissingWorkEmail
        or ReportingManagerRequired or ReportingManagerNotFound;
}

public sealed class BulkOnboardingResolutionState
{
    public List<BulkOnboardingValueMap> ValueMaps { get; set; } = [];
    public List<BulkOnboardingRowOverride> RowOverrides { get; set; } = [];
}

public sealed class BulkOnboardingValueMap
{
    public string Field { get; set; } = string.Empty;
    public string ImportedValue { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? NewValue { get; set; }
}

public sealed class BulkOnboardingRowOverride
{
    public int RowNumber { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> OriginalFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
