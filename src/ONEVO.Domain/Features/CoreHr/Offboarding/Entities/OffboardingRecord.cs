using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public static class OffboardingRecordStatuses
{
    public const string Initiated = "initiated";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>Tracks one employee's exit process end-to-end. See phase1-table-inventory.md
/// (Core HR, offboarding_records) for the documented baseline; RehireEligibility, Notes,
/// ChecklistTemplateId, InitiatedById, PreviousEmploymentStatusId, UpdatedAt, CompletedAt are
/// additions found missing during the 2026-08-17 offboarding-execution design (see
/// specs/next/2026-08-17-employee-offboarding-execution-backend-design.md §4.1).</summary>
public class OffboardingRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly LastWorkingDate { get; set; }
    public string KnowledgeRiskLevel { get; set; } = string.Empty;
    public string? RehireEligibility { get; set; }
    public string? Notes { get; set; }
    public Guid? ChecklistTemplateId { get; set; }
    public string? ExitInterviewNotes { get; set; }
    public string PenaltiesJson { get; set; } = "{}";
    public string Status { get; set; } = OffboardingRecordStatuses.Initiated;
    public Guid InitiatedById { get; set; }
    public int? PreviousEmploymentStatusId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
