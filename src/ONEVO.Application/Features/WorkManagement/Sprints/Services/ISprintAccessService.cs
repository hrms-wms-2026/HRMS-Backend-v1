using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

/// <summary>
/// Spec D5: a sprint can be managed (edit/start/complete/achieve) by its creator, or by anyone who
/// effectively owns (IsEffectiveOwnerAsync - own or ancestor) the Module of any task currently in it.
/// </summary>
public interface ISprintAccessService
{
    Task<bool> CanManageAsync(Guid tenantId, Sprint sprint, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default);

    Task<IReadOnlySet<Guid>> GetManageableSprintIdsAsync(
        Guid tenantId, Guid projectId, IReadOnlyList<Sprint> sprints, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default);

    /// <summary>Distinct active members of every Module that has a task in this sprint.</summary>
    Task<IReadOnlyList<Guid>> GetAudienceEmployeeIdsAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default);
}
