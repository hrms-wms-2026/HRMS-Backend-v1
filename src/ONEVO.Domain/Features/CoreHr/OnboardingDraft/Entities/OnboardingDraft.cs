using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class OnboardingDraft : BaseEntity
{
    public string EmployeeName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public Guid LegalEntityId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }
    public string EmploymentType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public string? EmployeeNumber { get; set; }

    // No enforced FK: work_schedules does not exist in this codebase yet. Documented Phase 1
    // limitation - see EMPLOYEE_MANAGEMENT_IMPLEMENTATION_REPORT.md.
    public Guid? ScheduleId { get; set; }

    // No enforced FK: checklist_templates does not exist in this codebase yet. Same limitation.
    public Guid? SelectedTemplateId { get; set; }
    public string? EditedTasksJson { get; set; }

    public string Status { get; set; } = OnboardingDraftStatus.Draft;
    public string? DraftReason { get; set; }
    public string LastSavedStep { get; set; } = OnboardingWizardStep.EmployeeDetails;
    public Guid StartedById { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
}

public static class OnboardingDraftStatus
{
    public const string Draft = "draft";
    public const string WaitingForSeat = "waiting_for_seat";
    public const string WaitingForPositionApproval = "waiting_for_position_approval";
    public const string Cancelled = "cancelled";
    public const string Finalized = "finalized";
}

public static class OnboardingDraftReason
{
    public const string SavedManually = "saved_manually";
    public const string WaitingForSeat = "waiting_for_seat";
    public const string WaitingForPositionApproval = "waiting_for_position_approval";
}

public static class OnboardingWizardStep
{
    public const string EmployeeDetails = "employee_details";
    public const string ChecklistTemplate = "checklist_template";
    public const string ChecklistReview = "checklist_review";
    public const string ReviewAndSubmit = "review_and_submit";
}
