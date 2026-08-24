using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class BulkOnboardingBatchRow : BaseEntity
{
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }
    public string RawDataJson { get; set; } = "{}";
    public Guid? ResolvedDepartmentId { get; set; }
    public Guid? ResolvedPositionId { get; set; }
    public Guid? ResolvedReportsToEmployeeId { get; set; }
    public Guid? ResolvedTemplateId { get; set; }
    public int? ResolvedWorkModeId { get; set; }
    public string Status { get; set; } = BulkOnboardingBatchRowStatus.PendingMapping;
    public string? ErrorMessage { get; set; }
    public Guid? OnboardingDraftId { get; set; }
}

public static class BulkOnboardingBatchRowStatus
{
    public const string PendingMapping = "pending_mapping";
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string DraftCreated = "draft_created";
    public const string DraftFailed = "draft_failed";
    public const string Finalized = "finalized";
    public const string WaitingForSeat = "waiting_for_seat";
    public const string WaitingForPositionApproval = "waiting_for_position_approval";
    public const string FinalizeFailed = "finalize_failed";
}
