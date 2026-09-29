using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public sealed record TaskAccessContext(WorkTask Task, Guid CallerEmployeeId);

/// <summary>
/// The exact task-visibility check GetTaskByIdQueryHandler performs, extracted
/// so every comment handler (which all need the identical check) doesn't
/// duplicate it. Returns Forbidden for auth/tenant problems, NotFound for a
/// task that doesn't exist or that the caller can't see — matching
/// GetTaskByIdQueryHandler's existing "never leak existence" behavior.
/// </summary>
public interface ITaskAccessResolver
{
    Task<Result<TaskAccessContext>> ResolveViewableTaskAsync(
        Guid tenantId, Guid userId, Guid taskId, CancellationToken ct = default);
}
