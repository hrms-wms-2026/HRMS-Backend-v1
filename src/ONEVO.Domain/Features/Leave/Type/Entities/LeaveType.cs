using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Type.Entities;

/// <summary>The kind of leave only (name, colour, paid, who it is for, documents). Every amount and
/// rule lives on the leave policy versions for this type.</summary>
public class LeaveType : ITenantOwnedEntity
{
    public const string DefaultColor = "#3B82F6";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Color { get; set; } = DefaultColor;
    public bool IsPaid { get; set; } = true;
    public bool RequiresDocument { get; set; }
    public decimal? DocumentRequiredAfterHours { get; set; }
    public string[] AcceptedDocumentTypes { get; set; } = [];
    public string ApplicableGender { get; set; } = Common.LeaveGenderRestrictions.All;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
