using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

/// <summary>
/// A Legal Entity's max task rate: the most of their working time a team can realistically turn into task hours (e.g.
/// 0.90). The overtime forecast calls work that needs a higher rate "overload". One row per Legal Entity; without a
/// row the code default applies (TaskRateLimit.Default). Edited from Settings (UI to come).
/// </summary>
public class TaskRateSetting : BaseEntity
{
    public Guid LegalEntityId { get; set; }

    /// <summary>A fraction, numeric(5,4): 0.9000 = 90%. Read values are clamped to 0.5..1.0.</summary>
    public decimal MaxTaskRate { get; set; }
    public Guid? UpdatedByEmployeeId { get; set; }
}
