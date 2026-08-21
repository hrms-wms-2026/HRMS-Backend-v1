using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

public class LeavePolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Country { get; set; }
    public string? JobLevel { get; set; }
    public string AccrualStart { get; set; } = Common.LeaveAccrualStarts.Immediately;
    public int? AccrualAfterNMonths { get; set; }
    public string ProrationMethod { get; set; } = Common.LeaveProrationMethods.CalendarDays;
    public bool ProbationRestriction { get; set; }
    public int MinimumTenureMonths { get; set; }
    public decimal? FirstYearReducedPercent { get; set; }
    public int MinimumNoticeDays { get; set; }
    public int? MaxConsecutiveDays { get; set; }
    public decimal MinDaysPerRequest { get; set; } = 0.5m;
    public decimal? MaxTeamAbsencePercent { get; set; }
    public string ApprovalMode { get; set; } = Common.LeaveApprovalModes.AnyOne;
    public DateOnly EffectiveFrom { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
