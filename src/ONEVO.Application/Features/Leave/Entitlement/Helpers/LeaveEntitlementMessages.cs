namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public static class LeaveEntitlementMessages
{
    public const string DuplicateEmployeeTypeYear =
        "Cannot duplicate the same employee, leave type, and year.";

    public const string NoPolicyAssigned =
        "No leave policy assigned to employee legal entity";

    public const string InactiveEmployee = "Employee is inactive";

    public const string YearUnavailable = "Balance data is not available for the selected year.";

    public const string EmployeeNotFound = "Employee was not found.";

    public const string LeaveTypeNotFound = "The selected leave type no longer exists.";

    public const string EntitlementNotFound = "Leave entitlement was not found.";

    public const string NoEmployeeRecord = "No employee record is linked to the current user.";

    public static string AlreadyExists(int year) =>
        $"Entitlements already exist for {year}. Use Recalculate to update";

    public static string NegativeRemaining(decimal newEntitlementDays, decimal usedDays) =>
        $"New entitlement ({newEntitlementDays:0.#} days) is less than already used ({usedDays:0.#} days). Employee will show negative balance";

    public static string LegalEntityChanged(DateOnly on) =>
        $"Employee changed legal entity on {on:yyyy-MM-dd}";
}
