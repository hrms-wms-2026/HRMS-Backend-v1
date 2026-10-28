using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Common.Services;

/// <summary>Adds a completed-hours delta to a module, every ancestor module and the project (tracked entities; the
/// caller saves). Shared by task completion and meeting hours stamping.</summary>
public static class CompletedHoursCascade
{
    public static async Task ApplyAsync(IObjectiveRepository objectives, IProjectRepository projects, Guid tenantId,
        Objective startingObjective, decimal delta, CancellationToken ct)
    {
        if (delta == 0m)
            return;

        var current = startingObjective;
        while (true)
        {
            current.CompletedHours += delta;

            if (current.ParentObjectiveId is null)
            {
                var project = await projects.GetTrackedByIdForTenantAsync(tenantId, current.ProjectId, ct);
                if (project is not null)
                    project.CompletedHours += delta;
                return;
            }

            var parent = await objectives.GetTrackedByIdForTenantAsync(tenantId, current.ParentObjectiveId.Value, ct);
            if (parent is null)
                return;

            current = parent;
        }
    }
}
