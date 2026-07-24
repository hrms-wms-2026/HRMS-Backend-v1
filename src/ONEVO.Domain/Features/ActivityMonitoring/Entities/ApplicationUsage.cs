using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class ApplicationUsage : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string? ApplicationCategory { get; set; }
    public string? WindowTitleHash { get; set; }
    public int TotalSeconds { get; set; }
    public bool? IsProductive { get; set; }
    public bool? IsAllowed { get; set; }
}
