using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Type.Entities;

public class LeaveType : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = Common.LeaveTypeCategories.Custom;
    public bool IsPaid { get; set; } = true;
    public bool RequiresApproval { get; set; } = true;
    public bool RequiresDocument { get; set; }
    public int? DocumentRequiredAfterDays { get; set; }
    public string[] AcceptedDocumentTypes { get; set; } = [];
    public int? MaxConsecutiveDays { get; set; }
    public decimal DefaultDaysPerYear { get; set; }
    public bool CarryForwardAllowed { get; set; }
    public decimal? MaxCarryForwardDays { get; set; }
    public int? CarryForwardExpiryMonths { get; set; }
    public bool ProRataForNewJoiners { get; set; }
    public string ApplicableGender { get; set; } = Common.LeaveGenderRestrictions.All;
    public int MinimumNoticeDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
