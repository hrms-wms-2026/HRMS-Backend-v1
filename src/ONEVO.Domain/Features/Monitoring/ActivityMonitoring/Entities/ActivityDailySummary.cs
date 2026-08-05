using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

/// <summary>
/// Pre-aggregated per-employee daily rollup of activity snapshots.
/// Unique on (tenant_id, employee_id, date).
/// </summary>
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
    public int DocumentTimeMinutes { get; set; }
    public int DeepFocusSessionsCount { get; set; }
    public string DataSource { get; set; } = "agent_windows";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
