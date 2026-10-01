namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

/// <summary>Who hears about a sprint's lifecycle events (completed, achieved, overdue).</summary>
public interface ISprintAudienceResolver
{
    /// <summary>Distinct active members of every Module that has a task in this sprint.</summary>
    Task<IReadOnlyList<Guid>> GetAudienceEmployeeIdsAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default);
}
