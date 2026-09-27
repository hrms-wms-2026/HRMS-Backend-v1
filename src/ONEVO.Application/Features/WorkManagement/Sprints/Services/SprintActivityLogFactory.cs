using System.Text.Json;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public static class SprintActivityLogFactory
{
    public static SprintActivityLog Create(
        Guid tenantId, Guid sprintId, Guid employeeId, string action,
        string? fromStatus = null, string? toStatus = null, object? details = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new SprintActivityLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SprintId = sprintId, EmployeeId = employeeId,
            Action = action, FromStatus = fromStatus, ToStatus = toStatus,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            OccurredAt = now, CreatedAt = now
        };
    }
}
