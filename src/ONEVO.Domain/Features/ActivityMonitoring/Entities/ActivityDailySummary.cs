using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class ActivityDailySummary : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public int TotalActiveMinutes { get; set; }
    public int TotalIdleMinutes { get; set; }
    public int TotalMeetingMinutes { get; set; }
    public decimal ActivePercentage { get; set; }
    public int ProductiveAppMinutes { get; set; }
    public int PersonalAppMinutes { get; set; }
    public int UnknownAppMinutes { get; set; }
    public int FocusMinutes { get; set; }
    public decimal ActivityScore { get; set; }
    public decimal DataCoveragePercentage { get; set; }
    public string TopAppsJson { get; set; } = "[]";
    public decimal IntensityAvg { get; set; }
    public int KeyboardTotal { get; set; }
    public int MouseTotal { get; set; }
}
