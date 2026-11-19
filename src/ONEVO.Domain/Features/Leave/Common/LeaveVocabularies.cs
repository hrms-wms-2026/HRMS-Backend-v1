namespace ONEVO.Domain.Features.Leave.Common;

public static class LeaveGenderRestrictions
{
    public const string All = "all";
    public const string Male = "male";
    public const string Female = "female";

    public static readonly string[] AllValues = [All, Male, Female];
}

public static class LeaveHalfDayPeriods
{
    public const string? None = null;
    public const string Am = "am";
    public const string Pm = "pm";
}

public static class LeaveApprovalModes
{
    public const string AnyOne = "any_one";
    public const string AllMustApprove = "all_must_approve";
    public const string InOrder = "in_order";
    public const string None = "none";

    public static readonly string[] All = [AnyOne, AllMustApprove, InOrder, None];
}

public static class LeaveRequestApproverStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Skipped = "skipped";
    public const string Cancelled = "cancelled";
}

public static class LeaveRequestDayAllocationStatuses
{
    public const string Active = "active";
    public const string Cancelled = "cancelled";
}

public static class LeaveRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

public static class LeaveBalanceChangeTypes
{
    public const string Accrual = "accrual";
    public const string Deduction = "deduction";
    public const string CarryForward = "carry_forward";
    public const string Forfeiture = "forfeiture";
    public const string Adjustment = "adjustment";
}

public static class LeaveEntitlementSources
{
    public const string Auto = "auto";
    public const string Manual = "manual";
}

public static class LeaveAmountPeriods
{
    public const string Month = "month";
    public const string Year = "year";

    public static readonly string[] All = [Month, Year];
}

public static class LeaveEligibility
{
    public const string Immediately = "immediately";
    public const string AfterProbation = "after_probation";
    public const string AfterMonths = "after_months";

    public static readonly string[] All = [Immediately, AfterProbation, AfterMonths];
}

public static class LeavePolicyTargetKinds
{
    public const string LegalEntity = "legal_entity";
    public const string Department = "department";
    public const string Position = "position";
    public const string Employee = "employee";

    public static readonly string[] All = [LegalEntity, Department, Position, Employee];
}
