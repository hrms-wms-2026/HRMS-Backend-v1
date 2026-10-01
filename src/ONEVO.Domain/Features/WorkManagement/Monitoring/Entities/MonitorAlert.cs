using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

/// <summary>
/// One problem the project monitor found on a Module, sprint or task (e.g. over capacity, past its end
/// date). Open while ResolvedAt is null; (TargetId, RuleCode, SubjectEmployeeId) identifies the problem
/// across hourly runs, so its creator position is notified once, and again only if it comes back after
/// being resolved.
/// </summary>
public class MonitorAlert : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string TargetTitle { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    /// <summary>The employee the problem is about (deadline overload), otherwise null.</summary>
    public Guid? SubjectEmployeeId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset FirstDetectedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }
}
