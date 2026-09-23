namespace ONEVO.Domain.Features.Leave.Common;

public static class LeaveTypeCategories
{
    public const string Annual = "annual";
    public const string Sick = "sick";
    public const string Maternity = "maternity";
    public const string Paternity = "paternity";
    public const string Compassionate = "compassionate";
    public const string Unpaid = "unpaid";
    public const string Custom = "custom";

    public static readonly string[] All =
        [Annual, Sick, Maternity, Paternity, Compassionate, Unpaid, Custom];
}

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

    public static readonly string[] All = [AnyOne, AllMustApprove, InOrder];
}

public static class LeaveRequestApproverStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Skipped = "skipped";
    public const string InformationRequested = "information_requested";
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
    public const string InformationRequested = "information_requested";
}

public static class LeaveBalanceChangeTypes
{
    public const string Accrual = "accrual";
    public const string Deduction = "deduction";
    public const string CarryForward = "carry_forward";
    public const string Forfeiture = "forfeiture";
    public const string Adjustment = "adjustment";
}

public static class LeaveAccrualStarts
{
    public const string Immediately = "immediately";
    public const string AfterProbation = "after_probation";
    public const string AfterNMonths = "after_n_months";

    public static readonly string[] All = [Immediately, AfterProbation, AfterNMonths];
}

public static class LeaveAccrualMethods
{
    public const string Annual = "annual";
    public const string Monthly = "monthly";
    public const string Daily = "daily";

    public static readonly string[] All = [Annual, Monthly, Daily];
}

public static class LeaveProrationMethods
{
    public const string CalendarDays = "calendar_days";
    public const string WorkingDays = "working_days";

    public static readonly string[] All = [CalendarDays, WorkingDays];
}

public static class LeaveEntitlementSources
{
    public const string Auto = "auto";
    public const string Manual = "manual";
}
