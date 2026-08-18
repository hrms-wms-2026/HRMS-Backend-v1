using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class BulkOnboardingBatch : BaseEntity
{
    public Guid LegalEntityId { get; set; }
    public string? DefaultEmploymentType { get; set; }
    public int? DefaultWorkModeId { get; set; }
    public Guid? DefaultChecklistTemplateId { get; set; }
    public string? ColumnMappingJson { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string Status { get; set; } = BulkOnboardingBatchStatus.MappingPending;
    public int TotalRows { get; set; }
    public int? ValidRows { get; set; }
    public int? InvalidRows { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public static class BulkOnboardingBatchStatus
{
    public const string MappingPending = "mapping_pending";
    public const string Validated = "validated";
    public const string DraftCreationPending = "draft_creation_pending";
    public const string DraftsCreated = "drafts_created";
    public const string FinalizePending = "finalize_pending";
    public const string FinalizeCompleted = "finalize_completed";
}
